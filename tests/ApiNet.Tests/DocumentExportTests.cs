using System.Text.Json.Nodes;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Documents;
using ApiNet.Infrastructure.OpenApi;

namespace ApiNet.Tests;

public class DocumentExportTests
{
    private static WorkspaceDocument Workspace() => new()
    {
        Folders = [new() { Id = "a", Name = "账户" }, new() { Id = "child", ParentId = "a", Name = "用户" }, new() { Id = "b", Name = "订单" }],
        Requests = [new() { Id = "one", FolderId = "a", Url = "{{baseUrl}}/one" }, new() { Id = "two", FolderId = "child", Url = "{{baseUrl}}/two" }, new() { Id = "three", FolderId = "b", Url = "{{baseUrl}}/three" }, new() { Id = "root", Url = "{{baseUrl}}/root" }]
    };

    [Fact]
    public void FolderSelectionIncludesDescendantsAndDeduplicatesOverlappingRequests()
    {
        var selected = WorkspaceExportSelection.Create(Workspace(), ["a", "child", "two", "three"]);
        Assert.Equal(["one", "two", "three"], selected.Requests.Select(x => x.Id));
        Assert.Equal(["a", "child", "b"], selected.Folders.Select(x => x.Id));
    }

    [Fact]
    public void IndividualSelectionKeepsAncestorsWithoutIncludingTheirOtherRequests()
    {
        var selected = WorkspaceExportSelection.Create(Workspace(), ["two"]);
        Assert.Equal("two", Assert.Single(selected.Requests).Id);
        Assert.Equal(["a", "child"], selected.Folders.Select(x => x.Id));
        Assert.Equal("账户 / 用户", RequestDocumentBuilder.FolderPath(selected, "child"));
        Assert.Empty(WorkspaceExportSelection.Create(Workspace(), []).Requests);
        Assert.Equal(4, WorkspaceExportSelection.Create(Workspace(), null).Requests.Count);
    }

    [Fact]
    public void CyclicFoldersTerminateAndMissingIdsDoNotExpandTheScope()
    {
        var workspace = Workspace();
        workspace.Folders[0].ParentId = "child";
        var selected = WorkspaceExportSelection.Create(workspace, ["child", "missing"]);
        Assert.Equal(2, selected.Requests.Count);
        Assert.Equal("账户 / 用户", RequestDocumentBuilder.FolderPath(selected, "child"));
        Assert.Empty(WorkspaceExportSelection.Create(workspace, ["missing"]).Requests);
    }

    [Fact]
    public void ExportSnapshotDoesNotChangeWhenWorkspaceIsEdited()
    {
        var workspace = Workspace();
        workspace.SharedVariables.Add(new() { Key = "host", Value = "before" });
        workspace.Environments.Add(new() { Variables = [new() { Key = "token", Value = "private", IsSecret = true }] });
        workspace.Requests[0].SourceId = "source";
        workspace.ImportSources.Add(new() { Id = "source", DocumentJson = "before" });
        var selected = WorkspaceExportSelection.Create(workspace, ["a"]);
        workspace.SharedVariables[0].Value = "after";
        workspace.Environments[0].Variables.Clear();
        workspace.ImportSources[0].DocumentJson = "after";
        workspace.Requests[0].Url = "after";
        workspace.Folders[0].Name = "after";
        Assert.Equal("before", selected.SharedVariables[0].Value);
        Assert.Equal("private", Assert.Single(selected.Environments[0].Variables).Value);
        Assert.Equal("before", selected.ImportSources[0].DocumentJson);
        Assert.Equal("{{baseUrl}}/one", selected.Requests[0].Url);
        Assert.Equal("账户", selected.Folders[0].Name);
    }

    [Fact]
    public void MarkdownEscapesTablesAndFencesAndRemovesCredentialsAndFilePaths()
    {
        var workspace = new WorkspaceDocument
        {
            SharedVariables = [new() { Key = "token", Value = "very-private", IsSecret = true }],
            Requests = [new()
            {
                Name = "Upload [example]", Method = "POST", Url = "{{host}}/upload?access=very-private", BodyFormat = "Multipart",
                Headers = "Authorization: Bearer plaintext\nCookie: session=plaintext\nX-Trace: very-private",
                HeaderParameters = [new() { Key = "X-Api-Key", Value = "plaintext" }],
                BodyParameters = [new() { Key = "a|b", Value = "<tag>\nnext" }, new() { Key = "file", Type = "File", Value = "C:/private/file.txt" }]
            }, new() { Name = "Raw", Url = "wss://example.test/ws", Protocol = "WebSocket", Subprotocol = "example.v1", BodyFormat = "Text", Body = "```\nvery-private\n```" }]
        };
        var markdown = RequestDocumentBuilder.Markdown(RequestDocumentBuilder.Build(workspace));
        Assert.DoesNotContain("very-private", markdown);
        Assert.DoesNotContain("plaintext", markdown);
        Assert.DoesNotContain("C:/private", markdown);
        Assert.Contains("{{token}}", markdown);
        Assert.Contains("a&#124;b", markdown);
        Assert.Contains("&lt;tag&gt;<br>next", markdown);
        Assert.Contains("````text\n```", markdown.ReplaceLineEndings("\n"));
        Assert.Contains("WebSocket", markdown);
        Assert.Contains("example.v1", markdown);
        Assert.Equal("C:/private/file.txt", workspace.Requests[0].BodyParameters[1].Value);
    }

    [Fact]
    public void ImportedDocumentsIncludeResponsesAndTransitiveReferencedModels()
    {
        var service = new OpenApiService();
        var workspace = new WorkspaceDocument();
        const string spec = """
        {"openapi":"3.0.3","info":{"title":"Test","version":"1"},"paths":{"/users":{"get":{"description":"读取用户资料","responses":{"200":{"description":"成功","content":{"application/json":{"schema":{"$ref":"#/components/schemas/User"}}}}}}}},"components":{"schemas":{"User":{"type":"object","properties":{"friend":{"$ref":"#/components/schemas/User"},"address":{"$ref":"#/components/schemas/Address"}}},"Address":{"type":"object","description":"中文地址"},"Unused":{"type":"string","description":"UNRELATED_MODEL"}}}}
        """;
        service.Apply(workspace, service.Preview(workspace, spec, null, "source.json"), false);
        var markdown = RequestDocumentBuilder.Markdown(RequestDocumentBuilder.Build(workspace));
        Assert.Contains("读取用户资料", markdown);
        Assert.Contains("响应定义", markdown);
        Assert.Contains("引用模型与定义", markdown);
        Assert.Contains("中文地址", markdown);
        Assert.DoesNotContain("UNRELATED_MODEL", markdown);
    }

    [Fact]
    public void SelectedOpenApiRoundTripsOnlyChosenRequestsAndFolderHierarchy()
    {
        var service = new OpenApiService();
        var workspace = Workspace();
        workspace.Requests.Add(new() { Id = "socket", FolderId = "child", Protocol = "WebSocket", Method = "WS", Url = "wss://example.test/ws" });
        var selected = WorkspaceExportSelection.Create(workspace, ["two", "socket"]);
        var json = service.Export(selected, null);
        var root = JsonNode.Parse(json)!;
        Assert.Single(root["paths"]!.AsObject());
        Assert.NotNull(root["paths"]!["/two"]);
        var imported = new WorkspaceDocument();
        service.Apply(imported, service.Preview(imported, json, null, "selected.json"), false);
        Assert.Equal(2, imported.Requests.Count);
        Assert.Equal(2, imported.Folders.Count);
        Assert.All(imported.Requests, request => Assert.Equal("账户 / 用户", RequestDocumentBuilder.FolderPath(imported, request.FolderId)));
        Assert.Contains(imported.Requests, x => x.Protocol == "WebSocket");
    }
}
