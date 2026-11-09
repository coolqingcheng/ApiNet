using ApiNet.Core.Models;
using ApiNet.Infrastructure.Persistence;
using ApiNet.Infrastructure.Http;
namespace ApiNet.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task WorkspaceAndHistoryPersistAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), "apinet-store-" + Guid.NewGuid().ToString("N"));
        var store = new SqliteWorkspaceStore(path);
        var request = new RequestDefinition { Name = "Saved" };
        await store.SaveAsync(new WorkspaceDocument { Requests = [request], OpenTabs = [request] });
        var history = new HistoryEntry { Request = request, Response = new HttpResult { Body = "detail" } };
        await store.AddHistoryAsync(history);
        history.Response.StatusCode = 201;
        await store.AddHistoryAsync(history);
        var reopened = new SqliteWorkspaceStore(path);
        Assert.Equal("Saved", (await reopened.LoadAsync())!.Requests[0].Name);
        Assert.Equal(201, Assert.Single(await reopened.GetHistoryAsync()).Response.StatusCode);
        Assert.Equal("", (await reopened.GetHistoryAsync())[0].Response.Body);
        Assert.Equal("detail", (await reopened.GetHistoryDetailAsync(history.Id))!.Response.Body);
    }
    [Fact]
    public void HistoryRedactsHeadersAndSecretVariableValues()
    {
        var workspace = new WorkspaceDocument { SharedVariables = [new() { Key = "token", Value = "top-secret", IsSecret = true }] };
        var history = HistorySanitizer.Create(new RequestDefinition { Url = "https://example.com/?token=top-secret", Headers = "Authorization: Bearer abc", Body = "top-secret" }, new HttpResult { Headers = "Set-Cookie: session=abc", Body = "top-secret" }, workspace);
        Assert.DoesNotContain("top-secret", history.Request.Url + history.Request.Body + history.Response.Body);
        Assert.DoesNotContain("abc", history.Request.Headers + history.Response.Headers);
    }
}
