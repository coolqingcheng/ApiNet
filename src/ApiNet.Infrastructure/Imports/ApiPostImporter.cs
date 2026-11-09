using System.Globalization;
using System.Text.Json.Nodes;
using ApiNet.Core.Models;
using static ApiNet.Infrastructure.Imports.ImportContext;

namespace ApiNet.Infrastructure.Imports;

internal static class ApiPostImporter
{
    public static void Parse(ImportContext context, JsonObject root, string? folder)
    {
        if (root["records"] is JsonArray records)
        {
            var objects = records.OfType<JsonObject>().ToList();
            foreach (var group in objects.Where(x => x["target_id"] is not null && x["target_type"] is not null && x["history_id"] is null && x["result_id"] is null).GroupBy(x => Text(x, "project_id", "local")))
            {
                var current = group.GroupBy(x => Text(x, "target_id")).Select(x => x.OrderByDescending(Timestamp).First()).Where(x => !Flag(x["is_deleted"])).ToList();
                var project = new JsonObject { ["project_id"] = group.Key, ["name"] = "ApiPost 项目 " + group.Key[..Math.Min(8, group.Key.Length)], ["apis"] = new JsonArray(current.Select(x => x.DeepClone()).ToArray()) };
                Project(context, project, folder);
            }
            foreach (var setting in objects.Where(x => Text(x, "key") is "default_server" or "global_var_list" or "env_var_list"))
            {
                var data = setting["value"];
                if (data is JsonValue)
                {
                    try { data = JsonNode.Parse(Scalar(data)); } catch (System.Text.Json.JsonException) { continue; }
                }
                if (Text(setting, "key") == "default_server" && Text(data, "uri").Length > 0)
                    context.Variables([new() { Key = "baseUrl", Value = Text(data, "uri") }]);
                else context.Variables(Variables(data));
            }
            context.Warn("从本地缓存导入已保存的请求；云端未同步数据、历史和执行结果不会导入。");
            return;
        }
        if (root["data"] is JsonObject wrapped && wrapped["apis"] is JsonArray) root = wrapped;
        if (root["apis"] is JsonArray) Project(context, root, folder);
        else if (root["target_type"] is not null)
        {
            Project(context, new JsonObject { ["name"] = "ApiPost", ["apis"] = new JsonArray(root.DeepClone()) }, folder);
        }
    }
    private static decimal Timestamp(JsonObject value) => decimal.TryParse(Text(value, "updated_time", Text(value, "updated_at", Text(value, "created_at"))), NumberStyles.Any, CultureInfo.InvariantCulture, out var timestamp) ? timestamp : 0;
    private static void Project(ImportContext context, JsonObject project, string? parent)
    {
        var key = Text(project, "project_id", Text(project, "name", "project"));
        var folder = context.Folder("apipost:" + key, Text(project, "name", "ApiPost 项目"), parent);
        var entries = new List<JsonObject>();
        void Flatten(JsonArray nodes, string parentId, int depth)
        {
            if (depth > 64) throw new FormatException("ApiPost 文件夹层级超过 64 层。");
            foreach (var entry in nodes.OfType<JsonObject>())
            {
                var clone = entry.DeepClone().AsObject();
                if (clone["parent_id"] is null) clone["parent_id"] = parentId;
                if (clone["target_id"] is null) clone["target_id"] = Text(entry, "uuid", Text(entry, "name") + ":" + entries.Count);
                entries.Add(clone);
                if (entry["children"] is JsonArray children) Flatten(children, Text(clone, "target_id"), depth + 1);
            }
        }
        Flatten(project["apis"]!.AsArray(), "", 0);
        var folders = entries.Where(x => Text(x, "target_type") == "folder").ToDictionary(x => Text(x, "target_id"));
        var folderIds = new Dictionary<string, string>();
        string Map(string id, HashSet<string> visiting)
        {
            if (folderIds.TryGetValue(id, out var result)) return result;
            if (!folders.TryGetValue(id, out var node)) return folder;
            if (!visiting.Add(id) || visiting.Count > 64) throw new FormatException("ApiPost 文件夹包含循环引用或层级过深。");
            result = context.Folder(key + "/" + id, Text(node, "name", "文件夹"), Map(Text(node, "parent_id"), visiting));
            visiting.Remove(id); return folderIds[id] = result;
        }
        foreach (var id in folders.Keys) Map(id, []);
        var global = project["global"] as JsonObject;
        foreach (var entry in entries.Where(x => Text(x, "target_type") != "folder" && !Flag(x["is_deleted"])))
        {
            var type = Text(entry, "target_type", "api");
            if (type is not ("api" or "http" or "websocket" or "ws")) { context.Warn("未导入 ApiPost 节点类型：" + type); continue; }
            var source = entry["request"] as JsonObject ?? new();
            var request = new RequestDefinition { Name = Text(entry, "name", "请求"), FolderId = folderIds.GetValueOrDefault(Text(entry, "parent_id"), folder), Method = Text(entry, "method", "GET").ToUpperInvariant(), Url = Text(entry, "url", Text(source, "url")), Protocol = type is "ws" or "websocket" ? "WebSocket" : Text(entry, "protocol", "HTTP"), HeaderParameters = Parameters(source["header"], true), QueryParameters = Parameters(source["query"], true) };
            if (request.Url.Length > 0 && !request.Url.Contains("://") && !request.Url.StartsWith("{{")) request.Url = "{{baseUrl}}/" + request.Url.TrimStart('/');
            foreach (var parameter in Parameters(global?["global_param"]?["header"], true))
                if (request.HeaderParameters.All(x => !x.Key.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))) request.HeaderParameters.Add(parameter);
            foreach (var parameter in Parameters(global?["global_param"]?["query"], true))
                if (request.QueryParameters.All(x => x.Key != parameter.Key)) request.QueryParameters.Add(parameter);
            foreach (var path in Parameters(source["restful"], true))
            {
                request.Url = System.Text.RegularExpressions.Regex.Replace(request.Url, @"(?<!\{)\{" + System.Text.RegularExpressions.Regex.Escape(path.Key) + @"\}(?!\})", _ => "{{" + path.Key + "}}");
                context.Variables([new() { Key = path.Key, Value = path.Value }]);
            }
            var auth = source["auth"] as JsonObject;
            if (auth is null || Text(auth, "type") == "inherit") auth = global?["global_param"]?["auth"] as JsonObject;
            PostmanImporter.Auth(context, request, auth);
            if (source["body"] is JsonObject body)
            {
                var mode = Text(body, "mode", "none").ToLowerInvariant();
                if (mode is "json" or "xml" or "html" or "javascript" or "plain" or "raw" or "text") { request.Body = Text(body, "raw"); request.BodyFormat = BodyFormat(mode); }
                else if (mode is "urlencoded" or "x-www-form-urlencoded" or "form-data" or "formdata")
                {
                    request.BodyFormat = mode is "urlencoded" or "x-www-form-urlencoded" ? "Form URL Encoded" : "Multipart";
                    request.BodyParameters = context.FormParameters(body, true);
                    request.HeaderParameters.RemoveAll(x => x.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
                }
                else if (mode != "none") context.Warn("未导入 ApiPost 请求体类型：" + mode);
            }
            if (Objects(source["pre_tasks"]).Any() || Objects(source["post_tasks"]).Any()) context.Warn("ApiPost 前置/后置任务未导入，不会执行；依赖脚本的变量请手动配置。");
            if (Parameters(source["cookie"], true).Count > 0) context.Warn("ApiPost Cookie 参数需手动在 Headers 中配置。");
            context.Add(request, "apipost:" + key + "/" + Text(entry, "target_id"));
        }
        context.Variables(Variables(global?["global_vars"]));
        foreach (var env in Objects(global?["envs"]))
        {
            var variables = Variables(env["env_var_list"]).ToList();
            var servers = Objects(env["server_list"]).ToList();
            if (servers.FirstOrDefault() is { } server && Text(server, "uri").Length > 0) variables.Add(new() { Key = "baseUrl", Value = Text(server, "uri") });
            foreach (var item in servers.Where(x => Text(x, "server_id").Length > 0)) variables.Add(new() { Key = "server." + Text(item, "server_id"), Value = Text(item, "uri") });
            context.Variables(variables, key + "/" + Text(env, "env_id", Text(env, "name")), Text(env, "name", "ApiPost 环境"));
        }
    }
    private static IEnumerable<VariableDefinition> Variables(JsonNode? node)
    {
        if (node is JsonArray || node is JsonObject obj && obj["parameter"] is JsonArray) return VariableList(node, true);
        if (node is JsonObject values) return values.Where(x => x.Value is JsonValue).Select(x => new VariableDefinition { Key = x.Key, Value = Scalar(x.Value) });
        return [];
    }
}
