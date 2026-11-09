using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiNet.Core.Models;

namespace ApiNet.Infrastructure.Documents;

public static class RequestDocumentBuilder
{
    public static RequestDocument Build(WorkspaceDocument workspace, string title = "ApiNet 接口文档")
    {
        List<DocumentBlock> blocks = [];
        Dictionary<string, JsonNode?> sources = [];
        Dictionary<(string Source, string Reference), JsonNode> definitions = [];
        foreach (var group in workspace.Requests.GroupBy(x => FolderPath(workspace, x.FolderId)))
        {
            blocks.Add(new(DocumentBlockKind.Heading, group.Key, "folder"));
            foreach (var original in group)
            {
                var request = RequestExportSanitizer.Clean(original, workspace);
                blocks.Add(new(DocumentBlockKind.Heading, request.Name, "request"));
                blocks.Add(new(DocumentBlockKind.Paragraph, request.Protocol == "WebSocket" ? "WebSocket · " + request.Url : request.Method + "  " + request.Url));
                JsonNode? operation = null;
                if (workspace.ImportSources.FirstOrDefault(x => x.Id == request.SourceId && x.Format == "OpenAPI") is { } source && request.SourceKey is { } key)
                {
                    var separator = key.IndexOf(' ');
                    if (separator > 0)
                    {
                        try
                        {
                            if (!sources.TryGetValue(source.Id, out var root))
                                sources[source.Id] = root = JsonNode.Parse(source.DocumentJson);
                            var path = root?["paths"]?[key[(separator + 1)..]];
                            operation = path?[key[..separator].ToLowerInvariant()];
                            CollectReferences(operation, root, source.Id, definitions);
                            CollectReferences(path?["parameters"], root, source.Id, definitions);
                        }
                        catch (JsonException) { }
                    }
                }
                if (operation?["description"]?.GetValue<string>() is { Length: > 0 } description)
                    blocks.Add(new(DocumentBlockKind.Paragraph, RequestExportSanitizer.Text(description, workspace)));
                AddParameters("Query 参数", QueryUrl.Parameters(request));
                var headers = request.HeaderParameters.ToList();
                foreach (var line in request.Headers.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var separator = line.IndexOf(':');
                    if (separator > 0 && headers.All(x => !x.Key.Equals(line[..separator].Trim(), StringComparison.OrdinalIgnoreCase)))
                        headers.Add(new RequestParameter { Key = line[..separator].Trim(), Value = line[(separator + 1)..].Trim() });
                }
                AddParameters("Headers", headers);
                if (request.Protocol == "WebSocket" && request.Subprotocol.Length > 0)
                    blocks.Add(new(DocumentBlockKind.Paragraph, "子协议：" + request.Subprotocol));
                blocks.Add(new(DocumentBlockKind.Paragraph, "请求体 · " + request.BodyFormat));
                if (request.BodyFormat is "Multipart" or "Form URL Encoded") AddParameters("表单参数", request.BodyParameters);
                else blocks.Add(new(DocumentBlockKind.Code, request.Body.Length == 0 ? "（无请求体）" : request.Body, request.BodyFormat.ToLowerInvariant()));
                if (operation?["responses"] is { } responses)
                {
                    blocks.Add(new(DocumentBlockKind.Paragraph, "响应定义（来自导入文档）"));
                    blocks.Add(new(DocumentBlockKind.Code, RequestExportSanitizer.Text(Pretty(responses), workspace), "json"));
                }
            }
        }
        if (definitions.Count > 0)
        {
            blocks.Add(new(DocumentBlockKind.Heading, "引用模型与定义", "folder"));
            var sourceNumbers = definitions.Keys.Select(x => x.Source).Distinct().Select((id, index) => (id, number: index + 1)).ToDictionary(x => x.id, x => x.number);
            foreach (var (key, definition) in definitions)
            {
                blocks.Add(new(DocumentBlockKind.Heading, $"来源 {sourceNumbers[key.Source]} · {key.Reference}", "request"));
                blocks.Add(new(DocumentBlockKind.Code, RequestExportSanitizer.Text(Pretty(definition), workspace), "json"));
            }
        }
        return new(title, workspace.Requests.Count, blocks);

        void AddParameters(string name, IEnumerable<RequestParameter> parameters)
        {
            var rows = parameters.Select(x => new[] { x.Enabled ? "启用" : "停用", x.Key, x.Type == "File" ? "File" : "Text", x.Type == "File" ? "（请自行选择文件）" : x.Value }).ToList();
            if (rows.Count == 0) return;
            rows.Insert(0, ["状态", "KEY", "类型", "VALUE"]);
            blocks.Add(new(DocumentBlockKind.Paragraph, name));
            blocks.Add(new(DocumentBlockKind.Table, Rows: rows));
            foreach (var parameter in parameters.Where(x => x.Description.Length > 0))
                blocks.Add(new(DocumentBlockKind.Paragraph, parameter.Key + "：" + parameter.Description));
        }
    }
    private static string Pretty(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = Core.Serialization.AppJsonContext.Default });
    private static void CollectReferences(JsonNode? node, JsonNode? root, string source, Dictionary<(string Source, string Reference), JsonNode> definitions)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) && reference.StartsWith("#/", StringComparison.Ordinal) && !definitions.ContainsKey((source, reference)))
            {
                var target = root;
                foreach (var part in reference[2..].Split('/'))
                {
                    var key = Uri.UnescapeDataString(part).Replace("~1", "/").Replace("~0", "~");
                    target = target is JsonObject parent ? parent[key] : target is JsonArray array && int.TryParse(key, out var index) && index >= 0 && index < array.Count ? array[index] : null;
                    if (target is null) break;
                }
                if (target is not null)
                {
                    definitions[(source, reference)] = target;
                    CollectReferences(target, root, source, definitions);
                }
            }
            foreach (var child in obj) CollectReferences(child.Value, root, source, definitions);
        }
        else if (node is JsonArray array)
            foreach (var child in array) CollectReferences(child, root, source, definitions);
    }
    public static string FolderPath(WorkspaceDocument workspace, string? id)
    {
        List<string> names = []; HashSet<string> visited = [];
        while (id is not null && visited.Add(id) && workspace.Folders.FirstOrDefault(x => x.Id == id) is { } folder)
        { names.Add(folder.Name); id = folder.ParentId; }
        names.Reverse(); return names.Count == 0 ? "工作区根目录" : string.Join(" / ", names);
    }
    public static string Markdown(RequestDocument document)
    {
        var text = new StringBuilder().Append("# ").AppendLine(Escape(document.Title)).AppendLine().AppendLine($"共 {document.RequestCount} 个请求。环境变量保留为占位符，认证值及本地文件路径不导出。").AppendLine();
        foreach (var block in document.Blocks)
        {
            switch (block.Kind)
            {
                case DocumentBlockKind.Heading: text.Append(block.Language == "folder" ? "## " : "### ").AppendLine(Escape(block.Text)).AppendLine(); break;
                case DocumentBlockKind.Paragraph: text.AppendLine(Escape(block.Text)).AppendLine(); break;
                case DocumentBlockKind.Code:
                    var fence = new string('`', Math.Max(3, LongestRun(block.Text, '`') + 1));
                    var language = block.Language is "json" or "xml" or "html" or "javascript" ? block.Language : "text";
                    text.Append(fence).AppendLine(language).AppendLine(block.Text).AppendLine(fence).AppendLine(); break;
                case DocumentBlockKind.Table:
                    var rows = block.Rows!;
                    for (var index = 0; index < rows.Count; index++)
                    {
                        text.Append("| ").Append(string.Join(" | ", rows[index].Select(Escape))).AppendLine(" |");
                        if (index == 0) text.AppendLine("| --- | --- | --- | --- |");
                    }
                    text.AppendLine(); break;
            }
        }
        return text.ToString();
    }
    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\\", "\\\\").Replace("#", "\\#").Replace("|", "&#124;").Replace("`", "\\`").Replace("*", "\\*").Replace("_", "\\_").Replace("[", "\\[").Replace("]", "\\]").ReplaceLineEndings("<br>");
    private static int LongestRun(string text, char marker) { int longest = 0, current = 0; foreach (var value in text) { current = value == marker ? current + 1 : 0; longest = Math.Max(longest, current); } return longest; }
}
