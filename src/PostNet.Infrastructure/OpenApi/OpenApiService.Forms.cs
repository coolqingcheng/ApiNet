using System.Text.Json.Nodes;
using PostNet.Core.Models;

namespace PostNet.Infrastructure.OpenApi;

public sealed partial class OpenApiService
{
    private static void ReadForm(JsonObject root, JsonObject operation, List<JsonObject> parameters, RequestDefinition request)
    {
        if (Dereference(root, operation["requestBody"])?["content"] is JsonObject content)
        {
            var mime = new[] { "multipart/form-data", "application/x-www-form-urlencoded" }.FirstOrDefault(x => content[x] is not null);
            if (mime is null) return;
            request.BodyFormat = mime == "multipart/form-data" ? "Multipart" : "Form URL Encoded";
            SetFormContentType(request, mime);
            request.Body = "";
            var media = content[mime]!;
            if (Dereference(root, media["schema"])?["properties"] is JsonObject properties)
                foreach (var property in properties)
                {
                    var schema = Dereference(root, property.Value);
                    var file = Text(schema, "format") == "binary" || Text(Dereference(root, schema?["items"]), "format") == "binary";
                    var sample = media["example"]?[property.Key] ?? Example(root, schema, 0);
                    request.BodyParameters.Add(new() { Key = property.Key, Type = file ? "File" : "Text", Value = file || sample is null ? "" : Scalar(sample), Enabled = !file, Description = Text(schema, "description") ?? "" });
                }
        }
        else if (parameters.Where(x => Text(x, "in") == "formData").ToList() is { Count: > 0 } form)
        {
            var consumes = operation["consumes"] as JsonArray ?? root["consumes"] as JsonArray;
            request.BodyFormat = form.Any(x => Text(x, "type") == "file") || consumes?.Any(x => x?.GetValue<string>() == "multipart/form-data") == true ? "Multipart" : "Form URL Encoded";
            request.Body = "";
            SetFormContentType(request, Http.HttpRequestService.BodyContentType(request.BodyFormat));
            foreach (var parameter in form)
            {
                var file = Text(parameter, "type") == "file";
                var sample = parameter["example"] ?? parameter["default"];
                request.BodyParameters.Add(new() { Key = Text(parameter, "name") ?? "", Type = file ? "File" : "Text", Value = file || sample is null ? "" : Scalar(sample), Enabled = !file, Description = Text(parameter, "description") ?? "" });
            }
        }
    }
    private static void SetFormContentType(RequestDefinition request, string mime) => request.Headers = string.Join('\n', request.Headers.Split('\n').Where(x => x.Length > 0 && !x.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)).Prepend("Content-Type: " + mime));
}
