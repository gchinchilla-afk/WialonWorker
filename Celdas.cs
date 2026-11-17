using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WialonWorker;

// Convierte el texto de una celda de Wialon en número: duraciones a horas decimales ("1 days 02:30:00" → 26.5)
// y medidas al número que traen ("123.45 km" → 123.45). Las unidades (km, l, km/h) son las de la cuenta.
public static partial class Celdas
{
    public static double? Leer(JsonNode? celda)
    {
        // La celda puede venir como texto o como objeto {"t": texto, ...}.
        var t = (celda is JsonObject o ? o["t"]?.ToString() : celda?.ToString())?.Trim();
        if (string.IsNullOrEmpty(t)) return null;

        var d = Duracion().Match(t);
        if (d.Success)
        {
            var dias = d.Groups[1].Success ? int.Parse(d.Groups[1].Value) : 0;
            return Math.Round(dias * 24 + int.Parse(d.Groups[2].Value)
                + int.Parse(d.Groups[3].Value) / 60.0 + int.Parse(d.Groups[4].Value) / 3600.0, 4);
        }

        var n = Numero().Match(t);
        return n.Success ? double.Parse(n.Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"^(?:(\d+)\s*(?:days?|d)\s+)?(\d+):(\d{2}):(\d{2})$")]
    private static partial Regex Duracion();

    [GeneratedRegex(@"^-?\d+(?:\.\d+)?")]
    private static partial Regex Numero();

    public static void Probar()
    {
        Trace.Assert(Leer(JsonValue.Create("02:30:00")) == 2.5);
        Trace.Assert(Leer(JsonValue.Create("1 days 02:30:00")) == 26.5);
        Trace.Assert(Leer(JsonValue.Create("37:00:00")) == 37);
        Trace.Assert(Leer(new JsonObject { ["t"] = "123.45 km" }) == 123.45);
        Trace.Assert(Leer(JsonValue.Create("65 km/h")) == 65);
        Trace.Assert(Leer(JsonValue.Create("-----")) is null);
        Trace.Assert(Leer(JsonValue.Create("")) is null);
        Console.WriteLine("Celdas: OK");
    }
}
