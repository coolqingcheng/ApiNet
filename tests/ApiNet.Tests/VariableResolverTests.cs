using ApiNet.Core.Models;
using ApiNet.Infrastructure.Variables;
namespace ApiNet.Tests;

public class VariableResolverTests
{
    private readonly VariableResolver _resolver = new();
    [Fact]
    public void EnvironmentOverridesSharedAndSupportsNestedValues()
    {
        var environment = new EnvironmentDefinition { Variables = [new() { Key = "host", Value = "test.example.com" }] };
        var workspace = new WorkspaceDocument { SharedVariables = [new() { Key = "host", Value = "localhost" }, new() { Key = "baseUrl", Value = "https://{{host}}" }], Environments = [environment], SelectedEnvironmentId = environment.Id };
        Assert.Equal("https://test.example.com/users", _resolver.Resolve("{{baseUrl}}/users", workspace));
    }
    [Fact] public void MissingVariableBlocksExecution() => Assert.Throws<InvalidOperationException>(() => _resolver.Resolve("{{missing}}", new()));
    [Fact] public void CyclicVariablesDoNotOverflowStack() => Assert.Throws<InvalidOperationException>(() => _resolver.Resolve("{{a}}", new WorkspaceDocument { SharedVariables = [new() { Key = "a", Value = "{{b}}" }, new() { Key = "b", Value = "{{a}}" }] }));
    [Fact]
    public void ResolvingDoesNotMutateSavedRequest()
    {
        var request = new RequestDefinition { Url = "{{host}}/get" };
        Assert.Equal("http://localhost:8080/get", _resolver.ResolveRequest(request, new WorkspaceDocument { SharedVariables = [new() { Key = "host", Value = "localhost:8080" }] }).Url);
        Assert.Equal("{{host}}/get", request.Url);
    }

    [Theory]
    [InlineData("example.test/api", "http://example.test/api")]
    [InlineData("localhost:8080/api", "http://localhost:8080/api")]
    [InlineData("127.0.0.1:9000/api", "http://127.0.0.1:9000/api")]
    [InlineData("[::1]:9000/api", "http://[::1]:9000/api")]
    [InlineData("//example.test/api", "http://example.test/api")]
    [InlineData(" example.test/api?next=https://other.test/#part ", "http://example.test/api?next=https://other.test/#part")]
    [InlineData("http://example.test/api", "http://example.test/api")]
    [InlineData("https://example.test/api", "https://example.test/api")]
    [InlineData("HTTPS://example.test/api", "HTTPS://example.test/api")]
    public void HttpRequestDefaultsToHttpAfterResolvingEnvironmentUrl(string url, string expected)
    {
        var environment = new EnvironmentDefinition { Variables = [new() { Key = "url", Value = url }] };
        var workspace = new WorkspaceDocument { Environments = [environment], SelectedEnvironmentId = environment.Id };
        var request = new RequestDefinition { Url = "{{url}}" };
        Assert.Equal(expected, _resolver.ResolveRequest(request, workspace).Url);
        Assert.Equal("{{url}}", request.Url);
    }

    [Fact]
    public void HttpDefaultDoesNotChangeWebSocketAddresses()
    {
        foreach (var url in new[] { "ws://localhost:8080/socket", "wss://example.test/socket" })
            Assert.Equal(url, _resolver.ResolveRequest(new RequestDefinition { Protocol = "WebSocket", Url = "{{url}}" }, new WorkspaceDocument { SharedVariables = [new() { Key = "url", Value = url }] }).Url);
    }
}
