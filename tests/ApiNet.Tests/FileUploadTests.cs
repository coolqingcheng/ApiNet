using ApiNet.Core.Models;
using ApiNet.Infrastructure.Http;
using ApiNet.Infrastructure.OpenApi;
using ApiNet.Infrastructure.Variables;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace ApiNet.Tests;

public class FileUploadTests
{
    [Fact]
    public async Task MultipartStreamsBinaryFilesWithDuplicateKeysAndReleasesHandles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apinet-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "sample文件.bin"); var second = Path.Combine(directory, "second.bin");
        var bytes = Enumerable.Range(0, 256 * 1024 + 17).Select(x => (byte)(x % 256)).ToArray();
        await File.WriteAllBytesAsync(first, bytes); await File.WriteAllBytesAsync(second, [255, 0, 128, 1]);
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var received = false;
        Exception? serverError = null;
        server.Use(async (context, next) => { try { await next(context); } catch (Exception exception) { serverError = exception; context.Response.StatusCode = 500; } });
        server.MapPost("/upload", async context =>
        {
            var form = await context.Request.ReadFormAsync();
            Assert.Equal("汉字 &+=", form["description"].ToString());
            Assert.Equal(2, form.Files.Count); Assert.Equal(0, form["disabled"].Count);
            Assert.Equal("files", form.Files[0].Name); Assert.Equal("files", form.Files[1].Name);
            Assert.Equal("sample文件.bin", form.Files[0].FileName); Assert.Equal("application/octet-stream", form.Files[0].ContentType);
            using var data = new MemoryStream(); await form.Files[0].CopyToAsync(data); Assert.Equal(bytes, data.ToArray());
            data.SetLength(0); await form.Files[1].CopyToAsync(data); Assert.Equal(new byte[] { 255, 0, 128, 1 }, data.ToArray());
            received = true; await context.Response.WriteAsync("uploaded");
        });
        await server.StartAsync();
        var workspace = new WorkspaceDocument { SharedVariables = [new() { Key = "filePath", Value = first }, new() { Key = "note", Value = "汉字 &+=" }] };
        var request = new RequestDefinition { Url = NetworkTests.Address(server) + "/upload", Method = "POST", Headers = "Content-Type: multipart/form-data", BodyFormat = "Multipart", BodyParameters = [new() { Key = "description", Value = "{{note}}" }, new() { Type = "File", Key = "files", Value = "{{filePath}}" }, new() { Type = "File", Key = "files", Value = second }, new() { Type = "File", Key = "disabled", Value = "{{missing}}", Enabled = false }] };
        var result = await new HttpRequestService().SendAsync(new VariableResolver().ResolveRequest(request, workspace), CancellationToken.None);
        try
        {
            Assert.Null(serverError); Assert.Null(result.Error); Assert.Equal(200, result.StatusCode); Assert.True(received); Assert.Equal("uploaded", result.Body);
            using var exclusive = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { if (result.DownloadPath is not null) File.Delete(result.DownloadPath); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidUploadReportsUsefulErrorAndDisposesPreviouslyOpenedFiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            var request = new RequestDefinition { Method = "POST", Url = "http://127.0.0.1:1/upload", BodyFormat = "Multipart", BodyParameters = [new() { Key = "first", Type = "File", Value = path }, new() { Key = "missing", Type = "File", Value = path + ".missing" }] };
            var result = await new HttpRequestService().SendAsync(request, CancellationToken.None);
            Assert.Contains("上传文件不存在", result.Error); Assert.Contains("missing", result.Error);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            request.BodyFormat = "Form URL Encoded";
            result = await new HttpRequestService().SendAsync(request, CancellationToken.None);
            Assert.Contains("Multipart", result.Error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OpenApiExportsBinarySchemaWithoutLocalPathsAndRestoresUploadFields()
    {
        var service = new OpenApiService();
        var request = new RequestDefinition { Url = "https://example.test/upload", Method = "POST", BodyFormat = "Multipart", BodyParameters = [new() { Key = "note", Value = "hello" }, new() { Type = "File", Key = "files", Value = "/private/doc.bin" }, new() { Type = "File", Key = "files", Value = "C:\\private\\doc.bin" }] };
        var exported = service.Export(new() { Requests = [request] }, null);
        Assert.DoesNotContain("private", exported);
        var doc = JsonNode.Parse(exported)!;
        Assert.Equal("binary", doc["paths"]!["/upload"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!["properties"]!["files"]!["items"]!["format"]!.GetValue<string>());
        var workspace = new WorkspaceDocument(); service.Apply(workspace, service.Preview(workspace, exported, null, "roundtrip.json"), false);
        var imported = Assert.Single(workspace.Requests); Assert.Equal("Multipart", imported.BodyFormat);
        Assert.Equal(2, imported.BodyParameters.Count(x => x.Type == "File" && !x.Enabled && x.Value == ""));
        Assert.True(request.BodyParameters[1].Enabled); Assert.Equal("/private/doc.bin", request.BodyParameters[1].Value);
        doc["paths"]!["/upload"]!["post"]!.AsObject().Remove("x-apinet-request");
        var standard = service.Preview(new(), doc.ToJsonString(), null, "standard.json");
        var file = Assert.Single(standard.Changes[0].Incoming.BodyParameters, x => x.Type == "File"); Assert.False(file.Enabled); Assert.Empty(file.Value);
    }

    [Fact]
    public void Swagger2FileFormParametersImportAsMultipart()
    {
        const string json = """{"swagger":"2.0","info":{"title":"Upload","version":"1"},"paths":{"/upload":{"post":{"parameters":[{"in":"formData","name":"document","type":"file"},{"in":"formData","name":"note","type":"string","default":"hello"},{"in":"header","name":"X-Test","default":"kept"}],"responses":{"200":{"description":"OK"}}}}}}""";
        var request = new OpenApiService().Preview(new(), json, null, "swagger.json").Changes[0].Incoming;
        Assert.Equal("Multipart", request.BodyFormat); Assert.Contains("X-Test: kept", request.Headers);
        Assert.False(request.BodyParameters[0].Enabled); Assert.Equal("File", request.BodyParameters[0].Type); Assert.Equal("hello", request.BodyParameters[1].Value);
    }
}
