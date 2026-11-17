# WialonWorker

Worker de .NET 8 que cada día extrae de Wialon, por unidad, las horas de motor, ralentí y traslado, el combustible, las velocidades máxima y media y la distancia del día anterior. Lo guarda en `dbo.WialonResumenDiario` (SQL Server, base `ApisDB`).

## Configuración
1. Correr `sql/crear_tabla.sql` en `ApisDB`.
2. En `appsettings.json` (o con variables de entorno):
   - `ConnectionStrings:ApisDB`
   - `Wialon:Token` (o `Wialon__Token`). No subir el token al repositorio.
   - `Wialon:Host`: `https://hst-api.wialon.com` u otro, según el centro de datos.
   - `Wialon:HoraEjecucion`: por defecto `02:00`.
3. `dotnet run -- --columnas` lista las columnas de la tabla "Horas motor" de la cuenta; ajustar `Wialon:Columnas` si los nombres no coinciden.

## Uso
- `dotnet run -- --dia 2026-11-16`: extrae ese día y termina (se puede repetir, no duplica).
- `dotnet run -- --probar`: autoprueba del lector de celdas.
- `dotnet run`: queda corriendo y extrae el día anterior a la hora configurada.

## Instalar como servicio de Windows
```powershell
dotnet publish -c Release -o C:\Servicios\WialonWorker
sc.exe create WialonWorker binPath= "C:\Servicios\WialonWorker\WialonWorker.exe" start= auto
sc.exe start WialonWorker
```
