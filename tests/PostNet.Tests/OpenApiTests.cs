using System.Text.Json.Nodes;
using PostNet.Core.Models;
using PostNet.Infrastructure.OpenApi;
namespace PostNet.Tests;

public class OpenApiTests
{
    private readonly OpenApiService _service = new();
    [Fact]
    public void ParameterDescriptionsSurviveStandardOpenApiAndApiNetRoundTrips()
    {
        const string spec = """
            {"openapi":"3.0.3","info":{"title":"Descriptions","version":"1"},"paths":{"/upload":{"post":{
              "parameters":[{"in":"query","name":"page","example":"2","description":"页码"},{"in":"header","name":"X-Client","example":"ApiNet","description":"客户端标识"}],
              "requestBody":{"content":{"multipart/form-data":{"schema":{"type":"object","properties":{"name":{"type":"string","example":"Alice","description":"显示名称"},"avatar":{"type":"string","format":"binary","description":"用户头像"}}}}}},
              "responses":{"200":{"description":"OK"}}
            }}}}
            """;
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, spec, null, "descriptions.json"), false);
        AssertDescriptions(Assert.Single(workspace.Requests));
        var exported = JsonNode.Parse(_service.Export(workspace, null))!;
        var operation = exported["paths"]!["/upload"]!["post"]!;
        Assert.Contains(operation["parameters"]!.AsArray(), x => x?["in"]?.GetValue<string>() == "header" && x["description"]?.GetValue<string>() == "客户端标识");
        Assert.Equal("用户头像", operation["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!["properties"]!["avatar"]!["description"]!.GetValue<string>());
        foreach (var keepExtension in new[] { true, false })
        {
            var document = exported.DeepClone();
            if (!keepExtension) document["paths"]!["/upload"]!["post"]!.AsObject().Remove("x-apinet-request");
            var restored = new WorkspaceDocument();
            _service.Apply(restored, _service.Preview(restored, document.ToJsonString(), null, "roundtrip.json"), false);
            AssertDescriptions(Assert.Single(restored.Requests));
        }

        static void AssertDescriptions(RequestDefinition request)
        {
            Assert.Equal("页码", Assert.Single(request.QueryParameters).Description);
            Assert.Equal("客户端标识", request.HeaderParameters.Single(x => x.Key == "X-Client").Description);
            Assert.Equal("显示名称", request.BodyParameters.Single(x => x.Key == "name").Description);
            var file = request.BodyParameters.Single(x => x.Key == "avatar");
            Assert.Equal("用户头像", file.Description); Assert.Equal("File", file.Type); Assert.False(file.Enabled);
        }
    }

    [Fact]
    public void SavingParsedQueryDoesNotCreateImportConflictsAndRemoteQueriesStillRefresh()
    {
        const string json = """{"openapi":"3.0.3","info":{"title":"Test","version":"1"},"paths":{"/v1":{"get":{"operationId":"same","parameters":[{"in":"query","name":"page","example":1}],"responses":{"200":{"description":"OK"}}}}}}""";
        var workspace = new WorkspaceDocument(); _service.Apply(workspace, _service.Preview(workspace, json, null, "online.json"), false);
        var request = Assert.Single(workspace.Requests);
        QueryUrl.Normalize(request); request.Name = "Local name";
        var changed = json.Replace("/v1", "/v2").Replace("\"example\":1", "\"example\":2");
        var plan = _service.Preview(workspace, changed, null, "online.json"); Assert.Empty(Assert.Single(plan.Changes).ConflictFields);
        _service.Apply(workspace, plan, false);
        Assert.Equal("Local name", request.Name); Assert.Equal("{{baseUrl}}/v2", request.Url); Assert.Equal("2", Assert.Single(request.QueryParameters).Value);
    }
    [Fact]
    public void ExportRoundTripsFormatsAndDisabledRowsWithoutExportingHeaderSecrets()
    {
        var request = new RequestDefinition { Method = "POST", Url = "https://example.com/xml", BodyFormat = "XML", Body = "<host>{{baseUrl}}</host>", QueryParameters = [new() { Key = "page", Value = "2" }, new() { Key = "draft", Value = "3", Enabled = false }], HeaderParameters = [new() { Key = "X-Sample", Value = "abc", Enabled = false }, new() { Key = "Authorization", Value = "secret" }] };
        var exported = _service.Export(new WorkspaceDocument { Requests = [request] }, null);
        var document = JsonNode.Parse(exported)!;
        Assert.NotNull(document["paths"]!["/xml"]!["post"]!["requestBody"]!["content"]!["application/xml"]);
        Assert.DoesNotContain("secret", exported);
        var imported = new WorkspaceDocument();
        _service.Apply(imported, _service.Preview(imported, exported, null, "roundtrip.json"), false);
        var restored = Assert.Single(imported.Requests);
        Assert.Equal("XML", restored.BodyFormat);
        Assert.Equal(2, restored.QueryParameters.Count);
        Assert.False(restored.QueryParameters[1].Enabled);
        Assert.False(Assert.Single(restored.HeaderParameters).Enabled);
        Assert.Equal(request.Body, restored.Body);
    }
    private static string Spec(string summary = "List users", string path = "/users", bool include = true)
    {
        var root = JsonNode.Parse("""{"openapi":"3.0.3","info":{"title":"Test","version":"1.0"},"servers":[{"url":"https://example.com"}],"paths":{"/users":{"get":{"summary":"List users","operationId":"listUsers","tags":["Users"],"responses":{"200":{"description":"OK","content":{"application/json":{"schema":{"$ref":"#/components/schemas/User"}}}}}}}},"components":{"schemas":{"User":{"type":"object","properties":{"id":{"type":"integer"}}}}}}""")!;
        var paths = root["paths"]!.AsObject();
        var operation = paths["/users"]!.DeepClone();
        paths.Clear();
        if (include)
        {
            operation["get"]!["summary"] = summary;
            paths[path] = operation;
        }
        return root.ToJsonString();
    }
    [Fact]
    public void ImportBindsToTargetFolderAndIsIdempotent()
    {
        var folder = new FolderDefinition { Name = "Target" };
        var workspace = new WorkspaceDocument { Folders = [folder] };
        _service.Apply(workspace, _service.Preview(workspace, Spec(), folder.Id, "api.json"), false);
        var first = Assert.Single(workspace.Requests);
        var id = first.Id;
        Assert.Equal(folder.Id, workspace.Folders.Single(x => x.Id == first.FolderId).ParentId);
        _service.Apply(workspace, _service.Preview(workspace, Spec(), folder.Id, "api.json"), false);
        Assert.Equal(id, Assert.Single(workspace.Requests).Id);
        Assert.Single(workspace.ImportSources);
    }
    [Fact]
    public void ThreeWayMergePreservesLocalFieldsAndDetectsConflicts()
    {
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, Spec(), null, "api.json"), false);
        var request = workspace.Requests[0];
        request.Name = "Local name";
        request.Headers = "Authorization: Bearer {{token}}";
        var plan = _service.Preview(workspace, Spec("Upstream name"), null, "api.json");
        Assert.Contains("Name", Assert.Single(plan.Changes).ConflictFields);
        _service.Apply(workspace, plan, false);
        Assert.Equal("Local name", request.Name);
        Assert.Contains("{{token}}", request.Headers);
    }
    [Fact]
    public void OperationIdRecognizesUniquePathRename()
    {
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, Spec(), null, "api.json"), false);
        var id = workspace.Requests[0].Id;
        _service.Apply(workspace, _service.Preview(workspace, Spec(path: "/v2/users"), null, "api.json"), false);
        Assert.Equal(id, Assert.Single(workspace.Requests).Id);
        Assert.Equal("{{baseUrl}}/v2/users", workspace.Requests[0].Url);
    }
    [Fact]
    public void RemovedEndpointsAreMarkedAndManualRequestsSurvive()
    {
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, Spec(), null, "api.json"), false);
        var manual = new RequestDefinition();
        workspace.Requests.Add(manual);
        _service.Apply(workspace, _service.Preview(workspace, Spec(include: false), null, "api.json"), false);
        Assert.Equal(2, workspace.Requests.Count);
        Assert.True(workspace.Requests[0].SourceRemoved);
        Assert.False(manual.SourceRemoved);
    }
    [Fact]
    public void ExportKeepsReferencesAndRoundTripsFoldersAndSockets()
    {
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, Spec(), null, "api.json"), false);
        workspace.Requests.Add(new RequestDefinition { Name = "Socket", Protocol = "WebSocket", Method = "WS", Url = "wss://example.com/ws", FolderId = workspace.Folders[0].Id });
        var export = _service.Export(workspace, null);
        var doc = JsonNode.Parse(export)!;
        var reference = doc["paths"]!["/users"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>();
        Assert.NotNull(doc["components"]!["schemas"]![reference.Split('/').Last()]);
        var other = new WorkspaceDocument();
        _service.Apply(other, _service.Preview(other, export, null, "export.json"), false);
        Assert.Equal(2, other.Requests.Count);
        Assert.Single(other.Folders);
        Assert.Contains(other.Requests, x => x.Protocol == "WebSocket");
    }
    [Fact]
    public void ExportDoesNotLeakSensitiveVariablesOrHeaders()
    {
        var workspace = new WorkspaceDocument { SharedVariables = [new() { Key = "token", Value = "secret-12345", IsSecret = true }], Requests = [new() { Url = "https://example.com/?token=secret-12345", Headers = "Authorization: Bearer secret-12345", Body = "secret-12345" }] };
        Assert.DoesNotContain("secret-12345", _service.Export(workspace, null));
    }
    [Fact]
    public void ImportDoesNotReuseUnrelatedEnvironmentHost()
    {
        var environment = new EnvironmentDefinition { Variables = [new() { Key = "baseUrl", Value = "https://unrelated.example.com" }] };
        var workspace = new WorkspaceDocument { Environments = [environment], SelectedEnvironmentId = environment.Id };
        _service.Apply(workspace, _service.Preview(workspace, Spec(), null, "api.json"), false);
        var resolved = new PostNet.Infrastructure.Variables.VariableResolver().ResolveRequest(workspace.Requests[0], workspace);
        Assert.Equal("https://example.com/users", resolved.Url);
    }
    [Fact]
    public void ManualPathAndQueryParametersAreStandardOpenApiParameters()
    {
        var workspace = new WorkspaceDocument { Requests = [new() { Url = "{{baseUrl}}/users/{{id}}?page=2" }] };
        var root = JsonNode.Parse(_service.Export(workspace, null))!;
        var parameters = root["paths"]!["/users/{id}"]!["get"]!["parameters"]!.AsArray();
        Assert.Contains(parameters, x => x?["in"]?.GetValue<string>() == "path" && x["required"]!.GetValue<bool>());
        Assert.Contains(parameters, x => x?["name"]?.GetValue<string>() == "page");
    }
    [Fact]
    public void Swagger2ImportsAndConvertsBodyDefinitionsOnExport()
    {
        const string spec = """{"swagger":"2.0","info":{"title":"Test","version":"1"},"host":"example.com","basePath":"/api","paths":{"/users":{"post":{"parameters":[{"name":"body","in":"body","schema":{"$ref":"#/definitions/User"}}],"responses":{"200":{"description":"OK","schema":{"$ref":"#/definitions/User"}}}}}},"definitions":{"User":{"type":"object","properties":{"name":{"type":"string"}}}}}""";
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, spec, null, "swagger.json"), false);
        Assert.Contains("name", workspace.Requests[0].Body);
        Assert.Equal("https://example.com/api", workspace.SharedVariables[0].Value);
        var exported = JsonNode.Parse(_service.Export(workspace, null))!;
        Assert.NotNull(exported["paths"]!["/users"]!["post"]!["requestBody"]);
    }
}
