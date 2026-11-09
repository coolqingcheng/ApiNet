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
        Assert.Equal("http://localhost/get", _resolver.ResolveRequest(request, new WorkspaceDocument { SharedVariables = [new() { Key = "host", Value = "http://localhost" }] }).Url);
        Assert.Equal("{{host}}/get", request.Url);
    }
}
