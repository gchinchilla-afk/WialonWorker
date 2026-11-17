using System.Text.Json;
using System.Text.Json.Nodes;

namespace WialonWorker;

// Cliente mínimo del Remote API de Wialon: todo va por POST a /wialon/ajax.html con svc, params y sid.
public class WialonCliente(HttpClient http, IConfiguration config)
{
    readonly string _host = config["Wialon:Host"]!.TrimEnd('/');
    string? _sid;

    public async Task Login()
    {
        var r = await Llamar("token/login", new JsonObject { ["token"] = config["Wialon:Token"], ["fl"] = 1 });
        _sid = r["eid"]!.GetValue<string>();
    }

    public async Task Logout()
    {
        if (_sid is null) return;
        try { await Llamar("core/logout", new JsonObject()); } catch { /* la sesión vence sola en 5 min */ }
        _sid = null;
    }

    public async Task<JsonArray> Buscar(string tipo) =>
        (JsonArray)(await Llamar("core/search_items", new JsonObject
        {
            ["spec"] = new JsonObject
            {
                ["itemsType"] = tipo, ["propName"] = "sys_name", ["propValueMask"] = "*", ["sortType"] = "sys_name"
            },
            ["force"] = 1, ["flags"] = 1, ["from"] = 0, ["to"] = 0
        }))["items"]!;

    public async Task<JsonNode> Llamar(string svc, JsonObject parametros)
    {
        var campos = new Dictionary<string, string> { ["svc"] = svc, ["params"] = parametros.ToJsonString() };
        if (_sid is not null) campos["sid"] = _sid;

        using var resp = await http.PostAsync($"{_host}/wialon/ajax.html", new FormUrlEncodedContent(campos));
        resp.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;

        // Wialon responde 200 aun con error: el error viene en el cuerpo como {"error": N}.
        if (json is JsonObject o && o["error"] is { } e && e.GetValue<int>() != 0)
            throw new WialonException(svc, e.GetValue<int>(), o["reason"]?.ToString());
        return json;
    }
}

public class WialonException(string svc, int codigo, string? razon)
    : Exception($"Wialon {svc} devolvió error {codigo}{(razon is null ? "" : $": {razon}")}")
{
    public int Codigo { get; } = codigo;
}
