using System.Text;
using System.Text.Json.Nodes;
using ApiNet.Core.Models;
using static ApiNet.Infrastructure.Imports.ImportContext;

namespace ApiNet.Infrastructure.Imports;

internal static class PostmanImporter
{
    public static void Parse(ImportContext context, JsonObject root, string? folder)
    {
        if (root["records"] is JsonArray records)
        {
            var objects = records.OfType<JsonObject>().Select(Unwrap).ToList();
            var collections = objects.Where(x => x["item"] is JsonArray || x["requests"] is JsonArray || x["folders_order"] is JsonArray).ToList();
            // Some Postman versions store collection metadata, folders and requests separately.
            foreach (var collection in objects.Where(x => x["order"] is JsonArray && x["url"] is null && Text(x, "collection", Text(x, "collectionId")).Length == 0 && !collections.Contains(x))) collections.Add(collection);
            foreach (var collection in collections)
            {
                var clone = collection.DeepClone().AsObject();
                if (clone["item"] is null && clone["requests"] is null)
                {
                    var id = Text(collection, "id");
                    clone["requests"] = new JsonArray(objects.Where(x => x["url"] is not null && Text(x, "collection", Text(x, "collectionId")) == id).Select(x => x.DeepClone()).ToArray());
                    clone["folders"] = new JsonArray(objects.Where(x => x["order"] is JsonArray && Text(x, "collection", Text(x, "collectionId")) == id).Select(x => x.DeepClone()).ToArray());
                }
                Collection(context, clone, folder);
            }
            foreach (var env in objects.Where(x => x["values"] is JsonArray && x["url"] is null)) Environment(context, env);
            return;
        }
        if (root["collections"] is JsonArray)
        {
            foreach (var collection in Objects(root["collections"])) Collection(context, collection, folder);
            foreach (var env in Objects(root["environments"])) Environment(context, env);
            if (root["globals"] is JsonArray globals) context.Variables(VariableList(globals));
            else if (root["globals"] is JsonObject global) context.Variables(VariableList(global["values"]));
        }
        else if (root["values"] is JsonArray) Environment(context, root);
        else Collection(context, root, folder);
    }
    private static JsonObject Unwrap(JsonObject value)
    {
        if (value["value"] is JsonObject v && (v["item"] is not null || v["url"] is not null || v["values"] is not null)) return v;
        if (value["data"] is JsonObject data && (data["item"] is not null || data["url"] is not null || data["values"] is not null)) return data;
        return value;
    }
    private static void Environment(ImportContext context, JsonObject env)
    {
        var scope = Text(env, "_postman_variable_scope");
        if (scope == "globals" || env["name"] is null && env["workspace"] is not null) context.Variables(VariableList(env["values"]));
        else context.Variables(VariableList(env["values"]), Text(env, "id", Text(env, "name", "environment")), Text(env, "name", "Postman 环境"));
    }
    private static void Collection(ImportContext context, JsonObject root, string? parent)
    {
        var key = Text(root["info"], "_postman_id", Text(root, "id", Text(root["info"], "name", Text(root, "name", "collection"))));
        var folder = context.Folder("postman:" + key, Text(root["info"], "name", Text(root, "name", "Postman 集合")), parent);
        context.Variables(VariableList(root["variable"]));
        Scripts(context, root);
        if (root["item"] is JsonArray items) Walk(context, items, folder, key, root["auth"] as JsonObject);
        else Legacy(context, root, folder, key);
    }
    private static void Walk(ImportContext context, JsonArray items, string folder, string key, JsonObject? auth, int depth = 0)
    {
        if (depth > 64) throw new FormatException("文件夹层级超过 64 层。");
        var index = 0;
        foreach (var item in items.OfType<JsonObject>())
        {
            var itemKey = key + "/" + Text(item, "id", Text(item, "name", "item") + ":" + index++);
            Scripts(context, item);
            var inherited = item["auth"] as JsonObject ?? auth;
            if (item["item"] is JsonArray children) Walk(context, children, context.Folder(itemKey, Text(item, "name", "文件夹"), folder), itemKey, inherited, depth + 1);
            else if (item["request"] is JsonObject request) Request(context, request, Text(item, "name", "请求"), folder, itemKey, request["auth"] as JsonObject ?? inherited);
            else if (item["request"] is JsonValue url) Request(context, new JsonObject { ["url"] = url.DeepClone() }, Text(item, "name", "请求"), folder, itemKey, inherited);
        }
    }
    private static void Legacy(ImportContext context, JsonObject root, string folder, string key)
    {
        var folders = Objects(root["folders"]).ToDictionary(x => Text(x, "id"));
        var folderIds = new Dictionary<string, string>();
        string Map(string id, HashSet<string> visiting)
        {
            if (folderIds.TryGetValue(id, out var mapped)) return mapped;
            if (!folders.TryGetValue(id, out var node)) return folder;
            if (!visiting.Add(id)) throw new FormatException("Postman 文件夹包含循环引用。");
            var parent = Text(node, "folder");
            var result = context.Folder(key + "/" + id, Text(node, "name", "文件夹"), parent.Length > 0 ? Map(parent, visiting) : folder);
            visiting.Remove(id); return folderIds[id] = result;
        }
        foreach (var id in folders.Keys) Map(id, []);
        foreach (var req in Objects(root["requests"]))
        {
            var id = Text(req, "id", Text(req, "name"));
            var folderKey = Text(req, "folder");
            if (folderKey.Length == 0) folderKey = folders.FirstOrDefault(x => (x.Value["order"] as JsonArray ?? []).Any(x => Scalar(x) == id)).Key ?? "";
            var clone = req.DeepClone().AsObject();
            if (req["headers"] is JsonValue h) clone["header"] = new JsonArray(Http.HeaderParser.Parse(h.GetValue<string>()).Select(x => (JsonNode)new JsonObject { ["key"] = x.Key, ["value"] = x.Value }).ToArray());
            if (req["dataMode"] is not null) clone["body"] = new JsonObject { ["mode"] = Text(req, "dataMode"), ["raw"] = Text(req, "rawModeData"), ["urlencoded"] = req["data"]?.DeepClone(), ["formdata"] = req["data"]?.DeepClone() };
            Request(context, clone, Text(req, "name", "请求"), folderIds.GetValueOrDefault(folderKey, folder), key + "/" + id, clone["auth"] as JsonObject ?? root["auth"] as JsonObject);
        }
    }
    private static void Request(ImportContext context, JsonObject source, string name, string folder, string key, JsonObject? auth)
    {
        var url = source["url"];
        var raw = url is JsonValue ? Scalar(url) : Text(url, "raw");
        if (raw.Length == 0 && url is JsonObject u)
            raw = Text(u, "protocol", "https") + "://" + (u["host"] is JsonArray hosts ? string.Join('.', hosts.Select(x => Scalar(x))) : Text(u, "host")) + "/" + (u["path"] is JsonArray paths ? string.Join('/', paths.Select(x => Scalar(x))) : Text(u, "path"));
        var request = new RequestDefinition { Name = name, Method = Text(source, "method", "GET").ToUpperInvariant(), Url = raw, FolderId = folder, HeaderParameters = Parameters(source["header"]) };
        if (url is JsonObject urlObject)
        {
            if (urlObject["query"] is JsonArray query && query.Count > 0)
            {
                request.QueryParameters = Parameters(query);
                var q = raw.IndexOf('?'); if (q >= 0) request.Url = raw[..q];
            }
            foreach (var variable in Objects(urlObject["variable"]))
            {
                var variableKey = Text(variable, "key");
                if (variableKey.Length > 0) request.Url = System.Text.RegularExpressions.Regex.Replace(request.Url, @"(?<=/):" + System.Text.RegularExpressions.Regex.Escape(variableKey) + @"(?=/|$|[?#])", _ => "{{" + variableKey + "}}");
            }
            context.Variables(VariableList(urlObject["variable"]));
        }
        Auth(context, request, auth);
        if (source["body"] is JsonObject body)
        {
            var mode = Text(body, "mode");
            if (mode is "raw" or "") { request.Body = Text(body, "raw"); request.BodyFormat = BodyFormat(Text(body["options"]?["raw"], "language", "text")); }
            else if (mode is "urlencoded" or "formdata")
            {
                request.BodyFormat = mode == "urlencoded" ? "Form URL Encoded" : "Multipart";
                request.BodyParameters = Parameters(body[mode]);
                var parameters = Objects(body[mode]).ToArray();
                for (var i = 0; i < parameters.Length; i++)
                    if (Text(parameters[i], "type") == "file") { request.BodyParameters[i].Enabled = false; request.BodyParameters[i].Value = ""; context.Warn("文件上传项已停用：当前仅导入表单文本字段，请手动配置文件上传。"); }
                request.HeaderParameters.RemoveAll(x => x.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            }
            else context.Warn("未导入请求体类型：" + mode + "。请检查 " + name);
        }
        Scripts(context, source);
        context.Add(request, "postman:" + key);
    }
    internal static void Auth(ImportContext context, RequestDefinition request, JsonObject? auth)
    {
        if (auth is null) return;
        var type = Text(auth, "type").ToLowerInvariant();
        if (type is "" or "noauth" or "none" or "inherit") return;
        var data = auth[type];
        string Get(string key) => data is JsonArray ? Text(Objects(data).FirstOrDefault(x => Text(x, "key") == key), "value") : Text(data, key);
        if (type == "bearer") request.HeaderParameters.Add(new() { Key = "Authorization", Value = "Bearer " + (data is JsonValue ? Scalar(data) : Get("token")) });
        else if (type is "apikey" or "kv")
        {
            var target = Get("in") == "query" ? request.QueryParameters : request.HeaderParameters;
            target.Add(new() { Key = Get("key"), Value = Get("value") });
        }
        else if (type == "basic" && !(Get("username") + Get("password")).Contains("{{"))
            request.HeaderParameters.Add(new() { Key = "Authorization", Value = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Get("username") + ":" + Get("password"))) });
        else context.Warn("认证方式 " + type + " 需手动配置（含变量的 Basic 认证也需重新设置）。");
    }
    private static void Scripts(ImportContext context, JsonObject node)
    {
        if (node["event"] is JsonArray events && events.Count > 0 || Text(node, "preRequestScript").Length > 0 || Text(node, "tests").Length > 0)
            context.Warn("Postman 前置/测试脚本未导入，不会执行；依赖脚本生成的变量请手动配置。");
    }
}
