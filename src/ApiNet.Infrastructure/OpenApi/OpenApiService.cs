using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Core.Serialization;
using ApiNet.Infrastructure.Imports;

namespace ApiNet.Infrastructure.OpenApi;

public sealed partial class OpenApiService : IOpenApiService
{
    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete", "head", "options", "trace"];


    public ImportPlan Preview(WorkspaceDocument workspace, string json, string? folderId, string location)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("文档必须是 JSON 对象。");
        var version = Text(root, "openapi") ?? Text(root, "swagger");
        if (version is null || !(version.StartsWith("3.0") || version.StartsWith("3.1") || version == "2.0"))
            throw new FormatException("支持 Swagger 2.0、OpenAPI 3.0 / 3.1 JSON。");
        if (root["paths"] is not JsonObject paths)
            throw new FormatException("文档缺少 paths 对象。");
        var existingSource = workspace.ImportSources.FirstOrDefault(x => x.FolderId == folderId && x.Location == location);
        var source = new ImportSource { Id = existingSource?.Id ?? Guid.NewGuid().ToString("N"), FolderId = folderId, Location = location, DocumentJson = json };
        source.BaseUrlVariable = existingSource?.BaseUrlVariable ?? (workspace.SharedVariables.Concat(workspace.Environments.SelectMany(x => x.Variables)).Any(x => x.Key == "baseUrl") ? "import_" + source.Id[..6] + ".baseUrl" : "baseUrl");
        var plan = new ImportPlan { Source = source, BaseUrl = Server(root) };
        var folderMap = new Dictionary<string, string>();
        string MappedId(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Id + ":" + id)))[..32].ToLowerInvariant();
        if (root["x-apinet-folders"] is JsonArray exportedFolders)
        {
            foreach (var node in exportedFolders.OfType<JsonObject>())
                if (Text(node, "id") is { } id)
                    folderMap[id] = MappedId(id);
            foreach (var node in exportedFolders.OfType<JsonObject>())
            {
                var id = Text(node, "id");
                if (id is null || !folderMap.ContainsKey(id))
                    continue;
                var parent = Text(node, "parentId");
                var folder = new FolderDefinition { Id = folderMap[id], Name = Text(node, "name") ?? "导入文件夹", ParentId = parent is not null && folderMap.TryGetValue(parent, out var mapped) ? mapped : folderId };
                if (workspace.Folders.All(x => x.Id != folder.Id))
                    plan.NewFolders.Add(folder);
            }
        }

        List<RequestDefinition> incoming = [];
        foreach (var path in paths)
        {
            if (path.Value is not JsonObject item)
                continue;
            foreach (var method in Methods)
            {
                if (item[method] is not JsonObject operation)
                    continue;
                var targetFolder = folderId;
                if (Text(operation, "x-apinet-folder") is { } original && folderMap.TryGetValue(original, out var mapped))
                    targetFolder = mapped;
                else if (operation["tags"] is JsonArray tags && tags.FirstOrDefault()?.GetValue<string>() is { Length: > 0 } tag)
                {
                    var folder = workspace.Folders.Concat(plan.NewFolders).FirstOrDefault(x => x.ParentId == folderId && x.Name == tag);
                    if (folder is null)
                    {
                        folder = new FolderDefinition { Name = tag, ParentId = folderId };
                        plan.NewFolders.Add(folder);
                    }
                    targetFolder = folder.Id;
                }
                var parameters = (item["parameters"] as JsonArray ?? []).Concat(operation["parameters"] as JsonArray ?? []).Select(x => Dereference(root, x)).OfType<JsonObject>().ToList();
                var operationServer = Server(operation) ?? Server(item);
                var url = (operationServer is not null && operationServer != plan.BaseUrl ? operationServer.TrimEnd('/') : "{{" + source.BaseUrlVariable + "}}") + path.Key;
                foreach (var parameter in parameters.Where(x => Text(x, "in") == "path"))
                {
                    var name = Text(parameter, "name")!;
                    url = url.Replace("{" + name + "}", "{{" + name + "}}", StringComparison.Ordinal);
                }
                List<string> query = [];
                foreach (var parameter in parameters.Where(x => Text(x, "in") == "query"))
                {
                    var sample = parameter["example"] ?? parameter["schema"]?["default"] ?? parameter["default"];
                    if (sample is not null)
                        query.Add(Uri.EscapeDataString(Text(parameter, "name")!) + "=" + Uri.EscapeDataString(Scalar(sample)));
                    else if (parameter["required"]?.GetValue<bool>() == true)
                        query.Add(Uri.EscapeDataString(Text(parameter, "name")!) + "={{" + Text(parameter, "name") + "}}");
                }
                if (query.Count > 0)
                    url += "?" + string.Join('&', query);
                var body = "";
                var headers = "";
                if (Dereference(root, operation["requestBody"])?["content"] is JsonObject content && content.FirstOrDefault().Value is JsonObject media)
                {
                    var sample = media["example"] ?? (media["examples"] as JsonObject)?.FirstOrDefault().Value?["value"] ?? Example(root, media["schema"], 0);
                    body = sample is JsonValue ? Scalar(sample) : sample is null ? "" : Pretty(sample);
                    headers = "Content-Type: " + content.First().Key;
                }
                else if (parameters.FirstOrDefault(x => Text(x, "in") == "body") is { } bodyParameter)
                {
                    var sample = Example(root, bodyParameter["schema"], 0);
                    body = sample is null ? "" : Pretty(sample);
                    headers = "Content-Type: application/json";
                }
                foreach (var parameter in parameters.Where(x => Text(x, "in") == "header"))
                {
                    var sample = parameter["example"] ?? parameter["default"] ?? parameter["schema"]?["default"];
                    if (sample is not null)
                        headers += "\n" + Text(parameter, "name") + ": " + Scalar(sample);
                }
                var request = new RequestDefinition
                {
                    Name = Text(operation, "summary") ?? Text(operation, "operationId") ?? path.Key,
                    Method = method.ToUpperInvariant(),
                    Url = url,
                    FolderId = targetFolder,
                    Headers = headers.Trim(),
                    Body = body,
                    BodyFormat = headers.Contains("xml", StringComparison.OrdinalIgnoreCase) ? "XML" : headers.Contains("text/plain", StringComparison.OrdinalIgnoreCase) ? "Text" : "JSON",
                    SourceId = source.Id,
                    SourceKey = method.ToUpperInvariant() + " " + path.Key,
                    OperationId = Text(operation, "operationId")
                };
                ReadForm(root, operation, parameters, request);
                if (operation["x-apinet-request"] is JsonObject extension)
                {
                    request.Url = Text(extension, "url") ?? request.Url;
                    request.Headers = Text(extension, "headers") ?? request.Headers;
                    request.Body = Text(extension, "body") ?? request.Body;
                    request.BodyFormat = Text(extension, "bodyFormat") ?? request.BodyFormat;
                    request.QueryParameters = extension["queryParameters"]?.Deserialize(AppJsonContext.Default.ListRequestParameter) ?? [];
                    request.HeaderParameters = extension["headerParameters"]?.Deserialize(AppJsonContext.Default.ListRequestParameter) ?? [];
                    request.BodyParameters = extension["bodyParameters"]?.Deserialize(AppJsonContext.Default.ListRequestParameter) ?? [];
                }
                foreach (var file in request.BodyParameters.Where(x => x.Type == "File")) file.Enabled = false;
                if (request.BodyParameters.Any(x => x.Type == "File") && !plan.Warnings.Contains("上传文件字段需在导入后重新选择本地文件。")) plan.Warnings.Add("上传文件字段需在导入后重新选择本地文件。");
                incoming.Add(request);
            }
        }
        if (root["x-apinet-websockets"] is JsonArray sockets)
            foreach (var node in sockets)
            {
                var request = node?.Deserialize(AppJsonContext.Default.RequestDefinition);
                if (request is null)
                    continue;
                request.FolderId = request.FolderId is not null && folderMap.TryGetValue(request.FolderId, out var mapped) ? mapped : folderId;
                request.SourceKey = "WS " + request.Id;
                request.SourceId = source.Id;
                request.Id = Guid.NewGuid().ToString("N");
                incoming.Add(request);
            }
        RequestImportMerger.Preview(workspace, plan, incoming);
        return plan;
    }

    public void Apply(WorkspaceDocument workspace, ImportPlan plan, bool overwriteConflicts)
    {
        RequestImportMerger.Apply(workspace, plan, overwriteConflicts);
        var previousSource = workspace.ImportSources.FirstOrDefault(x => x.Id == plan.Source.Id);
        var oldServer = previousSource is null ? null : Server(JsonNode.Parse(previousSource.DocumentJson)!.AsObject())?.TrimEnd('/');
        workspace.ImportSources.RemoveAll(x => x.Id == plan.Source.Id);
        workspace.ImportSources.Add(plan.Source);
        if (!string.IsNullOrEmpty(plan.BaseUrl))
        {
            var variable = workspace.SharedVariables.FirstOrDefault(x => x.Key == plan.Source.BaseUrlVariable);
            if (variable is null)
                workspace.SharedVariables.Add(new VariableDefinition { Key = plan.Source.BaseUrlVariable, Value = plan.BaseUrl.TrimEnd('/') });
            else if (variable.Value == oldServer)
                variable.Value = plan.BaseUrl.TrimEnd('/');
        }
    }

    public string Export(WorkspaceDocument workspace, string? folderId)
    {
        var included = Descendants(workspace, folderId);
        var requests = workspace.Requests.Where(x => folderId is null || (x.FolderId is not null && included.Contains(x.FolderId))).ToList();
        var root = new JsonObject { ["openapi"] = "3.0.3", ["info"] = new JsonObject { ["title"] = workspace.Folders.FirstOrDefault(x => x.Id == folderId)?.Name ?? "ApiNet 工作区", ["version"] = "1.0.0" }, ["paths"] = new JsonObject(), ["components"] = new JsonObject() };
        var components = (JsonObject)root["components"]!;
        var sources = new Dictionary<string, JsonObject>();
        foreach (var source in workspace.ImportSources.Where(x => requests.Any(r => r.SourceId == x.Id)))
        {
            if (source.Format != "OpenAPI") continue;
            var document = JsonNode.Parse(source.DocumentJson)!.AsObject();
            sources[source.Id] = document;
            var buckets = document["components"] as JsonObject ?? new JsonObject { ["schemas"] = document["definitions"]?.DeepClone(), ["parameters"] = document["parameters"]?.DeepClone(), ["responses"] = document["responses"]?.DeepClone(), ["securitySchemes"] = document["securityDefinitions"]?.DeepClone() };
            foreach (var bucket in buckets)
                if (bucket.Value is JsonObject definitions)
                    foreach (var definition in definitions)
                    {
                        components[bucket.Key] ??= new JsonObject();
                        var clone = definition.Value?.DeepClone();
                        RewriteRefs(clone, source.Id);
                        components[bucket.Key]![Prefix(source.Id) + definition.Key] = clone;
                    }
        }
        var paths = (JsonObject)root["paths"]!;
        var sockets = new JsonArray();
        foreach (var request in requests)
        {
            var clean = Documents.RequestExportSanitizer.Clean(request, workspace);
            if (request.Protocol == "WebSocket")
            {
                sockets.Add(JsonSerializer.SerializeToNode(clean, AppJsonContext.Default.RequestDefinition));
                continue;
            }
            var url = clean.Url;
            var prefixEnd = url.IndexOf("}}", StringComparison.Ordinal);
            if (url.StartsWith("{{") && prefixEnd >= 0)
                url = "http://apinet.local" + url[(prefixEnd + 2)..];
            var queryIndex = url.IndexOf('?');
            var path = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? Uri.UnescapeDataString(parsed.AbsolutePath) : (queryIndex >= 0 ? url[..queryIndex] : url);
            if (!path.StartsWith('/'))
                path = "/" + path;
            path = path.Replace("{{", "{").Replace("}}", "}");
            var method = request.Method.ToLowerInvariant();
            if (!Methods.Contains(method))
                continue;
            paths[path] ??= new JsonObject();
            if (paths[path]![method] is not null)
                throw new InvalidOperationException($"存在重复接口 {request.Method} {path}，请缩小导出范围。");
            JsonObject operation = new();
            if (request.SourceId is not null && sources.TryGetValue(request.SourceId, out var source) && request.SourceKey is { } sourceKey)
            {
                var separator = sourceKey.IndexOf(' ');
                if (separator > 0 && source["paths"]?[sourceKey[(separator + 1)..]] is JsonObject sourcePath && sourcePath[sourceKey[..separator].ToLowerInvariant()] is JsonObject original)
                {
                    operation = original.DeepClone().AsObject();
                    if (source["swagger"] is not null)
                        ConvertSwaggerOperation(source, operation, sourcePath);
                    else if (sourcePath["parameters"] is JsonArray shared)
                        operation["parameters"] = new JsonArray(shared.Concat(operation["parameters"] as JsonArray ?? []).Select(x => x?.DeepClone()).ToArray());
                    RewriteRefs(operation, request.SourceId);
                }
            }
            operation["summary"] = request.Name;
            operation["operationId"] = "request_" + request.Id;
            operation["responses"] ??= new JsonObject { ["default"] = new JsonObject { ["description"] = "响应" } };
            var operationParameters = operation["parameters"] as JsonArray ?? new JsonArray();
            foreach (var segment in path.Split('{').Skip(1))
            {
                var end = segment.IndexOf('}');
                if (end < 0)
                    continue;
                var name = segment[..end];
                if (operationParameters.OfType<JsonObject>().All(x => Text(x, "in") != "path" || Text(x, "name") != name))
                    operationParameters.Add((JsonNode)new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" } });
            }
            if (parsed is not null)
                foreach (var parameter in parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = parameter.Split('=', 2);
                    var name = Uri.UnescapeDataString(pair[0]);
                    if (operationParameters.OfType<JsonObject>().All(x => Text(x, "in") != "query" || Text(x, "name") != name))
                        operationParameters.Add((JsonNode)new JsonObject { ["name"] = name, ["in"] = "query", ["schema"] = new JsonObject { ["type"] = "string" }, ["example"] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "" });
                }
            operation["parameters"] = operationParameters.Parent is null ? operationParameters : operationParameters.DeepClone();
            foreach (var parameter in clean.QueryParameters.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
            {
                var list = operation["parameters"]!.AsArray();
                var existing = list.OfType<JsonObject>().FirstOrDefault(x => Text(x, "in") == "query" && Text(x, "name") == parameter.Key);
                if (existing is not null) existing["example"] = parameter.Value;
                else list.Add((JsonNode)new JsonObject { ["name"] = parameter.Key, ["in"] = "query", ["schema"] = new JsonObject { ["type"] = "string" }, ["example"] = parameter.Value });
            }
            operation["x-apinet-folder"] = request.FolderId;
            operation["x-apinet-request"] = new JsonObject { ["url"] = clean.Url, ["headers"] = clean.Headers, ["body"] = clean.Body, ["bodyFormat"] = clean.BodyFormat, ["queryParameters"] = JsonSerializer.SerializeToNode(clean.QueryParameters, AppJsonContext.Default.ListRequestParameter), ["bodyParameters"] = JsonSerializer.SerializeToNode(clean.BodyParameters, AppJsonContext.Default.ListRequestParameter), ["headerParameters"] = JsonSerializer.SerializeToNode(clean.HeaderParameters, AppJsonContext.Default.ListRequestParameter) };
            operation["tags"] = new JsonArray(JsonValue.Create(workspace.Folders.FirstOrDefault(x => x.Id == request.FolderId)?.Name ?? "请求"));
            if (operation["requestBody"] is null && request.Body.Length > 0 && method is not ("get" or "head"))
            {
                JsonNode? example;
                try
                {
                    example = JsonNode.Parse(clean.Body);
                }
                catch (JsonException) { example = JsonValue.Create(clean.Body); }
                operation["requestBody"] = new JsonObject { ["content"] = new JsonObject { [Http.HttpRequestService.BodyContentType(clean.BodyFormat)] = new JsonObject { ["example"] = example } } };
            }
            if (clean.BodyFormat is "Form URL Encoded" or "Multipart" && method is not ("get" or "head"))
            {
                var properties = new JsonObject();
                foreach (var group in clean.BodyParameters.Where(x => (x.Enabled || x.Type == "File") && x.Key.Length > 0).GroupBy(x => x.Key))
                {
                    var parameter = group.First();
                    properties[group.Key] = parameter.Type == "File"
                        ? group.Count() > 1
                            ? new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["format"] = "binary" } }
                            : new JsonObject { ["type"] = "string", ["format"] = "binary" }
                        : new JsonObject { ["type"] = "string", ["example"] = parameter.Value };
                }
                operation["requestBody"] = new JsonObject { ["content"] = new JsonObject { [Http.HttpRequestService.BodyContentType(clean.BodyFormat)] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "object", ["properties"] = properties } } } };
            }
            if (parsed is not null && parsed.Host != "apinet.local")
                operation["servers"] = new JsonArray(new JsonObject { ["url"] = parsed.GetLeftPart(UriPartial.Authority) });
            paths[path]![method] = operation;
        }
        var folders = new JsonArray();
        foreach (var folder in workspace.Folders.Where(x => included.Contains(x.Id)))
            folders.Add((JsonNode)new JsonObject { ["id"] = folder.Id, ["name"] = folder.Name, ["parentId"] = folder.ParentId is not null && included.Contains(folder.ParentId) ? folder.ParentId : null });
        root["x-apinet-folders"] = folders;
        root["x-apinet-websockets"] = sockets;
        var baseUrl = workspace.Environments.FirstOrDefault(x => x.Id == workspace.SelectedEnvironmentId)?.Variables.LastOrDefault(x => x.Key == "baseUrl")?.Value ?? workspace.SharedVariables.LastOrDefault(x => x.Key == "baseUrl")?.Value ?? "http://localhost";
        root["servers"] = new JsonArray(new JsonObject { ["url"] = baseUrl });
        return Pretty(root);
    }

    private static void ConvertSwaggerOperation(JsonObject root, JsonObject operation, JsonObject path)
    {
        var parameters = (path["parameters"] as JsonArray ?? []).Concat(operation["parameters"] as JsonArray ?? []).Select(x => Dereference(root, x)).OfType<JsonObject>().ToList();
        var converted = new JsonArray();
        foreach (var parameter in parameters)
        {
            if (Text(parameter, "in") == "body")
                operation["requestBody"] = new JsonObject { ["required"] = parameter["required"]?.DeepClone(), ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = parameter["schema"]?.DeepClone() } } };
            else if (Text(parameter, "in") != "formData")
            {
                var clone = parameter.DeepClone().AsObject();
                if (clone["schema"] is null)
                {
                    var schema = new JsonObject();
                    foreach (var key in new[] { "type", "format", "items", "enum", "default", "minimum", "maximum" })
                        if (clone.Remove(key, out var value))
                            schema[key] = value;
                    clone["schema"] = schema;
                }
                converted.Add((JsonNode)clone);
            }
        }
        operation["parameters"] = converted;
        if (operation["responses"] is JsonObject responses)
            foreach (var response in responses.Select(x => x.Value).OfType<JsonObject>())
                if (response.Remove("schema", out var schema))
                    response["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = schema } };
        operation.Remove("consumes");
        operation.Remove("produces");
        operation.Remove("schemes");
    }

    private static void RewriteRefs(JsonNode? node, string sourceId)
    {
        if (node is JsonObject obj)
        {
            if (Text(obj, "$ref") is { } reference)
            {
                reference = reference.Replace("#/definitions/", "#/components/schemas/").Replace("#/parameters/", "#/components/parameters/").Replace("#/responses/", "#/components/responses/");
                if (reference.StartsWith("#/components/"))
                {
                    var parts = reference.Split('/');
                    if (parts.Length >= 4)
                    {
                        parts[3] = Prefix(sourceId) + parts[3];
                        reference = string.Join('/', parts);
                    }
                }
                obj["$ref"] = reference;
            }
            if (obj["security"] is JsonArray security)
                foreach (var requirement in security.OfType<JsonObject>())
                    foreach (var key in requirement.Select(x => x.Key).ToArray())
                    {
                        var value = requirement[key]?.DeepClone();
                        requirement.Remove(key);
                        requirement[Prefix(sourceId) + key] = value;
                    }
            foreach (var child in obj.ToArray())
                RewriteRefs(child.Value, sourceId);
        }
        else if (node is JsonArray array)
            foreach (var child in array)
                RewriteRefs(child, sourceId);
    }

    private static string Prefix(string id) => "s" + id[..Math.Min(8, id.Length)] + "_";
    private static HashSet<string> Descendants(WorkspaceDocument workspace, string? folderId)
    {
        var ids = folderId is null ? workspace.Folders.Select(x => x.Id).ToHashSet() : new HashSet<string> { folderId };
        bool changed;
        do
        {
            changed = false;
            foreach (var folder in workspace.Folders)
                if (folder.ParentId is not null && ids.Contains(folder.ParentId))
                    changed |= ids.Add(folder.Id);
        } while (changed);
        return ids;
    }
    private static string? Text(JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string Scalar(JsonNode node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();
    private static string? Server(JsonObject root) => root["servers"] is JsonArray servers ? Text(servers.FirstOrDefault(), "url") : Text(root, "host") is { } host ? (root["schemes"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? "https") + "://" + host + (Text(root, "basePath") ?? "") : null;
    private static JsonNode? Dereference(JsonObject root, JsonNode? node)
    {
        for (var i = 0; i < 16 && Text(node, "$ref") is { } reference; i++)
        {
            if (!reference.StartsWith("#/"))
                throw new FormatException("第一版要求使用内联或本地 $ref，请先将外部引用打包为单个 OpenAPI JSON。");
            node = root;
            foreach (var segment in reference[2..].Split('/'))
                node = node?[segment.Replace("~1", "/").Replace("~0", "~")];
        }
        return node;
    }
    private static JsonNode? Example(JsonObject root, JsonNode? schema, int depth)
    {
        if (depth > 4)
            return null;
        schema = Dereference(root, schema);
        if (schema is not JsonObject obj)
            return null;
        if (obj["example"] is { } example)
            return example.DeepClone();
        if (obj["default"] is { } fallback)
            return fallback.DeepClone();
        if (obj["enum"] is JsonArray enums && enums.Count > 0)
            return enums[0]?.DeepClone();
        if (obj["properties"] is JsonObject properties)
        {
            var result = new JsonObject();
            foreach (var property in properties)
                result[property.Key] = Example(root, property.Value, depth + 1);
            return result;
        }
        return Text(obj, "type") switch
        {
            "integer" or "number" => JsonValue.Create(0),
            "boolean" => JsonValue.Create(false),
            "array" => new JsonArray(),
            _ => JsonValue.Create("")
        };
    }
    private static string Pretty(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            node.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
