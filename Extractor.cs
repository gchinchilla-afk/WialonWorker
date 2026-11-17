using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace WialonWorker;

// Cada día a la hora configurada extrae el día anterior: un renglón por unidad con horas motor,
// ralentí, traslado, combustible, velocidades y distancia, usando la tabla "Horas motor" de Wialon.
public class Extractor(IServiceProvider sp, IConfiguration config, ILogger<Extractor> log) : BackgroundService
{
    const string Tabla = "unit_engine_hours";
    static readonly string[] Campos =
        ["HorasMotor", "HorasRalenti", "HorasTraslado", "Combustible", "VelocidadMaxima", "VelocidadMedia", "Distancia"];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var hora = TimeOnly.Parse(config["Wialon:HoraEjecucion"] ?? "02:00");
        while (!ct.IsCancellationRequested)
        {
            var ahora = DateTime.Now;
            var proxima = DateOnly.FromDateTime(ahora).ToDateTime(hora);
            if (proxima <= ahora) proxima = proxima.AddDays(1);
            log.LogInformation("Próxima extracción: {Proxima}", proxima);
            await Task.Delay(proxima - ahora, ct);

            try { await ExtraerDia(DateOnly.FromDateTime(DateTime.Now).AddDays(-1), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Falló la extracción"); }
        }
    }

    public async Task ExtraerDia(DateOnly dia, CancellationToken ct)
    {
        var columnas = config.GetSection("Wialon:Columnas");
        var plantilla = PlantillaDiaria(Campos.Select(c => columnas[c]!).ToArray());

        // ponytail: el día se corta en la zona horaria del servidor; la cuenta de Wialon debe tener la misma.
        var desde = new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue)).ToUnixTimeSeconds();
        var hasta = new DateTimeOffset(dia.ToDateTime(TimeOnly.MaxValue)).ToUnixTimeSeconds();

        var wialon = sp.GetRequiredService<WialonCliente>();
        await wialon.Login();
        try
        {
            // ponytail: usa el primer recurso visible; configurar uno fijo si la cuenta tiene varios y falla por permisos.
            var recurso = (await wialon.Buscar("avl_resource"))[0]!["id"]!.GetValue<long>();
            var unidades = await wialon.Buscar("avl_unit");

            var filas = new List<(long Id, string Nombre, double?[] Valores)>();
            foreach (var u in unidades)
            {
                ct.ThrowIfCancellationRequested();
                var id = u!["id"]!.GetValue<long>();
                var nombre = u["nm"]!.ToString();
                try
                {
                    var valores = await ReporteUnidad(wialon, recurso, id, desde, hasta, plantilla);
                    if (valores is null) { log.LogDebug("{Unidad} sin datos el {Dia}", nombre, dia); continue; }
                    filas.Add((id, nombre, valores));
                }
                catch (WialonException ex) { log.LogWarning("{Unidad}: {Error}", nombre, ex.Message); }
            }

            await Guardar(dia, filas, ct);
            log.LogInformation("{Dia}: {N} de {Total} unidades con datos guardadas en ApisDB", dia, filas.Count, unidades.Count);
        }
        finally { await wialon.Logout(); }
    }

    // Reemplaza el día completo: volver a extraer un día no duplica renglones.
    async Task Guardar(DateOnly dia, List<(long Id, string Nombre, double?[] Valores)> filas, CancellationToken ct)
    {
        await using var cn = new SqlConnection(config.GetConnectionString("ApisDB"));
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);

        var borrar = new SqlCommand("DELETE dbo.WialonResumenDiario WHERE Fecha = @Fecha", cn, tx);
        borrar.Parameters.AddWithValue("@Fecha", dia.ToDateTime(TimeOnly.MinValue));
        await borrar.ExecuteNonQueryAsync(ct);

        var insertar = new SqlCommand($"""
            INSERT dbo.WialonResumenDiario (Fecha, UnidadId, Unidad, {string.Join(", ", Campos)})
            VALUES (@Fecha, @UnidadId, @Unidad, {string.Join(", ", Campos.Select(c => "@" + c))})
            """, cn, tx);
        foreach (var (id, nombre, valores) in filas)
        {
            insertar.Parameters.Clear();
            insertar.Parameters.AddWithValue("@Fecha", dia.ToDateTime(TimeOnly.MinValue));
            insertar.Parameters.AddWithValue("@UnidadId", id);
            insertar.Parameters.AddWithValue("@Unidad", nombre.Length > 100 ? nombre[..100] : nombre);
            for (var i = 0; i < Campos.Length; i++)
                insertar.Parameters.AddWithValue("@" + Campos[i], (object?)valores[i] ?? DBNull.Value);
            await insertar.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    // Devuelve los valores de Campos en orden, o null si la unidad no tuvo actividad ese día.
    static async Task<double?[]?> ReporteUnidad(WialonCliente w, long recurso, long unidad, long desde, long hasta, JsonObject plantilla)
    {
        await w.Llamar("report/cleanup_result", new JsonObject()); // solo cabe un resultado por sesión

        var r = await w.Llamar("report/exec_report", new JsonObject
        {
            ["reportResourceId"] = recurso,
            ["reportTemplateId"] = 0,
            ["reportTemplate"] = plantilla.DeepClone(),
            ["reportObjectId"] = unidad,
            ["reportObjectSecId"] = 0,
            ["interval"] = new JsonObject { ["from"] = desde, ["to"] = hasta, ["flags"] = 0 }
        });

        var tablas = r["reportResult"]?["tables"]?.AsArray();
        if (tablas is null || tablas.Count == 0) return null;

        // Las etiquetas (cl) son los nombres de Campos; con agrupación Wialon antepone su propia columna,
        // por eso se ubica cada campo por su etiqueta en el encabezado y no por posición.
        var encabezado = tablas[0]!["header"]!.AsArray().Select(h => h!.ToString()).ToList();
        var filas = (await w.Llamar("report/get_result_rows", new JsonObject
        {
            ["tableIndex"] = 0, ["indexFrom"] = 0, ["indexTo"] = 0
        })).AsArray();
        if (filas.Count == 0) return null;

        var celdas = filas[0]!["c"]!.AsArray();
        return Campos.Select(c =>
        {
            var i = encabezado.IndexOf(c);
            return i < 0 || i >= celdas.Count ? null : Celdas.Leer(celdas[i]);
        }).ToArray();
    }

    // Plantilla en línea: tabla "Horas motor" agrupada por día. "p" es un JSON dentro de otro JSON (así lo pide Wialon).
    static JsonObject PlantillaDiaria(string[] columnas) => new()
    {
        ["id"] = 0, ["n"] = "extraccion_diaria", ["ct"] = "avl_unit", ["p"] = "",
        ["tbl"] = new JsonArray(new JsonObject
        {
            ["n"] = Tabla, ["l"] = "Horas motor",
            ["c"] = string.Join(',', columnas),
            ["cl"] = string.Join(',', Campos),
            ["s"] = "", ["sl"] = "",
            ["p"] = new JsonObject { ["grouping"] = new JsonObject { ["type"] = "day" }.ToJsonString() }.ToJsonString(),
            ["sch"] = new JsonObject { ["f1"] = 0, ["f2"] = 0, ["t1"] = 0, ["t2"] = 0, ["m"] = 0, ["y"] = 0, ["w"] = 0 },
            ["f"] = 0
        })
    };

    public async Task ListarColumnas()
    {
        var wialon = sp.GetRequiredService<WialonCliente>();
        await wialon.Login();
        try
        {
            var tablas = (await wialon.Llamar("report/get_report_tables", new JsonObject())).AsArray();
            var tabla = tablas.FirstOrDefault(t => t!["n"]?.ToString() == Tabla && t["ct"]?.ToString() == "avl_unit");
            if (tabla is null)
            {
                Console.WriteLine($"La cuenta no tiene {Tabla}. Tablas disponibles:");
                foreach (var t in tablas) Console.WriteLine($"  {t!["ct"]}  {t["n"]}  ({t["l"]})");
                return;
            }
            foreach (var col in tabla["col"]!.AsArray()) Console.WriteLine($"  {col!["n"],-30} {col["l"]}");
        }
        finally { await wialon.Logout(); }
    }
}
