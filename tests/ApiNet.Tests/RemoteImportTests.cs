using ApiNet.Core.Models;
using ApiNet.Infrastructure.Imports;
using ApiNet.Infrastructure.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApiNet.Tests;

public class RemoteImportTests
{
    [Fact]
    public async Task SwaggerUrlFollowsRedirectsAndRefreshesIncrementallyIntoSelectedFolder()
    {
        var document = """{"swagger":"2.0","host":"example.test","schemes":["https"],"info":{"title":"Demo","version":"1"},"paths":{"/users":{"get":{"operationId":"users","summary":"Original"}}}}""";
        await using var server = Server();
        server.MapGet("/swagger.json", () => Results.Text(document, "application/json"));
        server.MapGet("/docs", () => Results.Redirect("/swagger.json"));
        await server.StartAsync();
        var url = Address(server) + "/docs";
        var service = new WorkspaceImportService(new OpenApiService());
        var workspace = new WorkspaceDocument { Folders = [new() { Id = "target" }] };
        service.Apply(workspace, service.Preview(workspace, await service.ReadUrlAsync(url), "target", url), false);
        var request = Assert.Single(workspace.Requests); var id = request.Id;
        request.Body = "local";
        document = document.Replace("Original", "Refreshed");
        var plan = service.Preview(workspace, await service.ReadUrlAsync(url), "target", url);
        Assert.Equal("更新", Assert.Single(plan.Changes).Kind);
        service.Apply(workspace, plan, false);
        Assert.Equal(id, Assert.Single(workspace.Requests).Id);
        Assert.Equal("Refreshed", request.Name); Assert.Equal("local", request.Body);
        Assert.Equal("target", request.FolderId); Assert.Equal(url, Assert.Single(workspace.ImportSources).Location);
    }
    [Fact]
    public async Task UrlImportRejectsHtmlErrorsOversizedDocumentsAndNonHttpSchemes()
    {
        await using var server = Server();
        server.MapGet("/html", () => Results.Text("<html>Swagger UI</html>", "text/html"));
        server.MapGet("/forbidden", () => Results.StatusCode(403));
        server.MapGet("/unrelated", () => Results.Text("{\"value\":1}", "application/json"));
        server.MapGet("/large", () => Results.Bytes(new byte[OpenApiDocumentDownloader.MaximumDocumentBytes + 1], "application/json"));
        await server.StartAsync();
        var reader = new OpenApiDocumentDownloader(); var address = Address(server);
        var html = await Assert.ThrowsAsync<FormatException>(() => reader.ReadAsync(address + "/html"));
        Assert.Contains("Swagger UI", html.Message);
        await Assert.ThrowsAsync<HttpRequestException>(() => reader.ReadAsync(address + "/forbidden"));
        await Assert.ThrowsAsync<FormatException>(() => reader.ReadAsync(address + "/unrelated"));
        await Assert.ThrowsAsync<FormatException>(() => reader.ReadAsync(address + "/large"));
        await Assert.ThrowsAsync<FormatException>(() => reader.ReadAsync("file:///private/swagger.json"));
    }
    [Fact]
    public async Task DocumentDownloadHonorsCancellation()
    {
        await using var server = Server();
        server.MapGet("/slow", async context => { await Task.Delay(Timeout.Infinite, context.RequestAborted); });
        await server.StartAsync();
        using var cancellation = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenApiDocumentDownloader().ReadAsync(Address(server) + "/slow", cancellation.Token));
    }
    private static WebApplication Server()
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); return builder.Build();
    }
    private static string Address(WebApplication server) => server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
}
