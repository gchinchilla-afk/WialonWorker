using WialonWorker;

// Modos de consola para probar sin esperar a la hora programada:
//   dotnet run -- --columnas            lista las columnas de la tabla "Horas motor" de la cuenta
//   dotnet run -- --dia 2026-11-16      extrae ese día y termina
//   dotnet run -- --probar              autoprueba del lector de celdas
if (args.Contains("--probar")) { Celdas.Probar(); return; }

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "WialonWorker");
builder.Services.AddHttpClient<WialonCliente>();
builder.Services.AddHostedService<Extractor>();
var host = builder.Build();

var i = Array.IndexOf(args, "--dia");
if (args.Contains("--columnas") || i >= 0)
{
    var extractor = host.Services.GetServices<IHostedService>().OfType<Extractor>().Single();
    if (i >= 0) await extractor.ExtraerDia(DateOnly.Parse(args[i + 1]), default);
    else await extractor.ListarColumnas();
    return;
}

host.Run();
