using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ApiNet.Core.Models;

namespace ApiNet.Infrastructure.Imports;

internal sealed class ImportContext(WorkspaceDocument workspace, ImportPlan plan)
{
    public ImportPlan Plan { get; } = plan;
    public List<RequestDefinition> Requests { get; } = [];
    public string Id(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Plan.Source.Id + ":" + key)))[..32].ToLowerInvariant();
    public string Folder(string key, string name, string? parent)
    {
        var id = Id("folder:" + key);
        if (workspace.Folders.Concat(Plan.NewFolders).All(x => x.Id != id))
            Plan.NewFolders.Add(new FolderDefinition { Id = id, ParentId = parent, Name = name });
        return id;
    }
    public void Add(RequestDefinition request, string key)
    {
        request.SourceId = Plan.Source.Id;
        request.SourceKey = key;
        if (Requests.Any(x => x.SourceKey == key)) throw new FormatException("导入文档中存在重复请求标识，请检查来源数据。");
        request.Protocol = request.Url.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) || request.Url.StartsWith("wss:", StringComparison.OrdinalIgnoreCase) || request.Protocol.Equals("websocket", StringComparison.OrdinalIgnoreCase) ? "WebSocket" : "HTTP";
        if (request.Protocol == "WebSocket") request.Method = "WS";
        request.Headers = string.Join('\n', request.HeaderParameters.Where(x => x.Enabled && x.Key.Length > 0).Select(x => x.Key + ": " + x.Value));
        Requests.Add(request);
    }
    public void Warn(string warning) { if (!Plan.Warnings.Contains(warning)) Plan.Warnings.Add(warning); }
    public void Variables(IEnumerable<VariableDefinition> variables, string? environmentKey = null, string? environmentName = null)
    {
        var list = Plan.SharedVariables;
        if (environmentKey is not null)
        {
            var id = Id("environment:" + environmentKey);
            var env = Plan.Environments.FirstOrDefault(x => x.Id == id);
            if (env is null) { env = new EnvironmentDefinition { Id = id, Name = environmentName ?? "导入环境" }; Plan.Environments.Add(env); }
            list = env.Variables;
        }
        foreach (var variable in variables.Where(x => x.Key.Length > 0))
        {
            var old = list.FirstOrDefault(x => x.Key == variable.Key);
            if (old is null) list.Add(variable);
            else if (old.Value != variable.Value) Warn("来源内存在同名变量，保留首个值，请在管理变量中检查：" + variable.Key);
        }
    }
    public static string Text(JsonNode? node, string key, string fallback = "") => node is JsonObject obj ? Scalar(obj[key], fallback) : fallback;
    public static string Scalar(JsonNode? node, string fallback = "") => node is JsonValue value ? value.TryGetValue<string>(out var text) ? text : value.ToJsonString() : fallback;
    public static IEnumerable<JsonObject> Objects(JsonNode? node) => (node as JsonArray ?? []).OfType<JsonObject>();
    public static bool Flag(JsonNode? node) => node is JsonValue && Scalar(node) is "true" or "1";
    public static List<RequestParameter> Parameters(JsonNode? node, bool apiPost = false)
    {
        if (node is JsonObject obj) node = obj["parameter"];
        return Objects(node).Select(x => new RequestParameter { Key = Text(x, "key", Text(x, "name")), Value = Text(x, "value"), Enabled = apiPost ? x["is_checked"] is null || Flag(x["is_checked"]) : !Flag(x["disabled"]) && (x["enabled"] is null || Flag(x["enabled"])) }).ToList();
    }
    public static IEnumerable<VariableDefinition> VariableList(JsonNode? node, bool apiPost = false) => Parameters(node, apiPost).Where(x => x.Enabled).Select(x => new VariableDefinition { Key = x.Key, Value = x.Value, IsSecret = x.Key.Contains("token", StringComparison.OrdinalIgnoreCase) || x.Key.Contains("password", StringComparison.OrdinalIgnoreCase) || x.Key.Contains("secret", StringComparison.OrdinalIgnoreCase) });
    public static string BodyFormat(string type) => type.ToLowerInvariant() switch { "json" => "JSON", "xml" => "XML", "html" => "HTML", "javascript" => "JavaScript", _ => "Text" };
}
