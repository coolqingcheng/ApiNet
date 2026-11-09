using System.Text.Json.Nodes;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Imports;
using ApiNet.Infrastructure.OpenApi;

namespace ApiNet.Tests;

public class CollectionImportTests
{
    private readonly WorkspaceImportService _service = new(new OpenApiService());
    private const string Postman = """
        {"info":{"_postman_id":"demo","name":"集合"},"variable":[{"key":"host","value":"https://example.test"}],
         "auth":{"type":"bearer","bearer":[{"key":"token","value":"{{token}}"}]},
         "event":[{"listen":"prerequest","script":{"exec":["throw new Error('never run')"]}}],
         "item":[{"id":"folder","name":"用户","item":[
           {"id":"read","name":"查询","request":{"method":"GET","url":{"raw":"{{host}}/users?q={{term}}&off=1","query":[{"key":"q","value":"{{term}}"},{"key":"off","value":"{{missing}}","disabled":true}]},"header":[{"key":"X-Off","value":"{{missing}}","disabled":true}]}},
           {"id":"write","name":"更新","request":{"method":"POST","url":"{{host}}/users","body":{"mode":"raw","raw":"<user />","options":{"raw":{"language":"xml"}}}}},
           {"id":"duplicate","name":"同 URL 不同请求","request":{"url":"{{host}}/users"}},
           {"id":"ws","name":"订阅","request":{"url":"wss://example.test/events","auth":{"type":"noauth"}}}
         ]}]}
        """;
    [Fact]
    public void PostmanImportsStringAndRichDescriptionsForAllParameterTables()
    {
        const string json = """
            {"info":{"name":"Descriptions"},"item":[{"name":"Upload","request":{"method":"POST",
            "url":{"raw":"https://example.test/upload?page=2","query":[{"key":"page","value":"2","description":"页码"}]},
            "header":[{"key":"X-Client","value":"ApiNet","description":{"content":"客户端标识","type":"text/plain"}}],
            "body":{"mode":"formdata","formdata":[{"key":"avatar","type":"file","src":"avatar.png","description":"用户头像"},{"key":"name","value":"Alice","type":"text","description":"显示名称"}]}}}]}
            """;
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, json, null, "postman.json"), false);
        var request = Assert.Single(workspace.Requests);
        Assert.Equal("页码", Assert.Single(request.QueryParameters).Description);
        Assert.Equal("客户端标识", Assert.Single(request.HeaderParameters).Description);
        Assert.Equal("用户头像", request.BodyParameters.Single(x => x.Type == "File").Description);
        Assert.Equal("显示名称", request.BodyParameters.Single(x => x.Key == "name").Description);
    }

    [Fact]
    public void PostmanKeepsNestedFoldersDuplicateUrlsVariablesAndDisabledParameters()
    {
        var workspace = new WorkspaceDocument { Folders = [new() { Id = "target" }] };
        var plan = _service.Preview(workspace, Postman, "target", "demo.json");
        Assert.Empty(workspace.Requests);
        Assert.Equal(2, plan.NewFolders.Count);
        Assert.Equal("target", plan.NewFolders.Single(x => x.Name == "集合").ParentId);
        Assert.Equal(4, plan.Changes.Count);
        var read = plan.Changes.Single(x => x.Incoming.Name == "查询").Incoming;
        Assert.Equal("{{host}}/users", read.Url);
        Assert.False(read.QueryParameters[1].Enabled);
        Assert.False(read.HeaderParameters[0].Enabled);
        Assert.Contains("Bearer {{token}}", read.Headers);
        Assert.Equal("XML", plan.Changes.Single(x => x.Incoming.Name == "更新").Incoming.BodyFormat);
        Assert.Equal("WebSocket", plan.Changes.Single(x => x.Incoming.Name == "订阅").Incoming.Protocol);
        Assert.DoesNotContain("Authorization", plan.Changes.Single(x => x.Incoming.Name == "订阅").Incoming.Headers);
        Assert.Contains(plan.Warnings, x => x.Contains("脚本"));
        _service.Apply(workspace, plan, false);
        Assert.Equal("https://example.test", Assert.Single(workspace.SharedVariables).Value);
        Assert.Equal(4, workspace.Requests.Count);
    }
    [Fact]
    public void ReimportMergesByIdentityPreservesLocalConflictsAndMarksRemovedRequests()
    {
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, Postman, null, "demo.json"), false);
        var request = workspace.Requests.Single(x => x.Name == "查询");
        var originalId = request.Id;
        request.Name = "本地名称";
        request.Headers = "X-Local: local"; request.HeaderParameters = [new() { Key = "X-Local", Value = "local" }];
        var root = JsonNode.Parse(Postman)!;
        var children = root["item"]![0]!["item"]!.AsArray();
        children[0]!["name"] = "来源新名称";
        children[0]!["request"]!["url"]!["raw"] = "{{host}}/new";
        children.RemoveAt(1);
        var plan = _service.Preview(workspace, root.ToJsonString(), null, "demo.json");
        var conflict = plan.Changes.Single(x => x.ExistingId == originalId);
        Assert.Equal("冲突", conflict.Kind);
        Assert.Contains("Name", conflict.ConflictFields);
        Assert.Single(plan.Changes, x => x.Kind == "移除");
        _service.Apply(workspace, plan, false);
        Assert.Equal("本地名称", request.Name);
        Assert.Equal("{{host}}/new", request.Url);
        Assert.Equal("X-Local: local", request.Headers);
        Assert.True(workspace.Requests.Single(x => x.Name == "更新").SourceRemoved);
        var next = root.DeepClone(); next["item"]![0]!["item"]![0]!["name"] = "再次修改";
        _service.Apply(workspace, _service.Preview(workspace, next.ToJsonString(), null, "demo.json"), true);
        Assert.Equal("再次修改", request.Name);
        Assert.Equal(4, workspace.Requests.Count);
        Assert.Empty(_service.Preview(workspace, next.ToJsonString(), null, "demo.json").NewFolders);
    }
    [Fact]
    public void PostmanLegacyBackupAndEnvironmentOnlyImportsWork()
    {
        const string json = """
            {"version":1,"collections":[{"id":"c","name":"旧集合","folders":[{"id":"f","name":"子文件夹","order":["r"]}],"requests":[{"id":"r","name":"旧请求","method":"POST","url":"https://example.test","headers":"X-Demo: value","dataMode":"urlencoded","data":[{"key":"a","value":"{{v}}","enabled":true}]}]}],"environments":[{"id":"env","name":"开发","values":[{"key":"v","value":"one"},{"key":"disabled","value":"x","enabled":false}]}]}
            """;
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, json, null, "backup.json"), false);
        var req = Assert.Single(workspace.Requests);
        Assert.Equal(workspace.Folders.Single(x => x.Name == "子文件夹").Id, req.FolderId);
        Assert.Equal("Form URL Encoded", req.BodyFormat);
        Assert.Equal("X-Demo: value", req.Headers);
        Assert.Single(Assert.Single(workspace.Environments).Variables);
        const string env = """{"id":"env2","name":"独立环境","_postman_variable_scope":"environment","values":[{"key":"host","value":"https://one.test"}]}""";
        _service.Apply(workspace, _service.Preview(workspace, env, null, "env.json"), false);
        workspace.Environments.Last().Variables[0].Value = "local";
        var plan = _service.Preview(workspace, env.Replace("one.test", "two.test"), null, "env.json");
        Assert.Contains(plan.Warnings, x => x.Contains("保留本地值"));
        _service.Apply(workspace, plan, true);
        Assert.Equal(2, workspace.Environments.Count);
        Assert.Equal("local", workspace.Environments.Last().Variables[0].Value);
    }
    [Fact]
    public void ApiPostProjectImportsFlatHierarchyRawBodyGlobalParametersAndEnvironments()
    {
        const string json = """
            {"project_id":"p","name":"项目","global":{"global_vars":[{"key":"token","value":"secret","is_checked":1}],"global_param":{"header":{"parameter":[{"key":"X-Global","value":"yes","is_checked":1}]}},"envs":[{"env_id":"e","name":"开发","server_list":[{"server_id":"s","uri":"https://example.test"}],"env_var_list":[{"key":"id","value":"42","is_checked":true}]}]},"apis":[
             {"target_id":"f","target_type":"folder","name":"子目录","parent_id":"0"},
             {"target_id":"r","target_type":"api","parent_id":"f","name":"读取","method":"POST","url":"/users/{id}","request":{"header":{"parameter":[{"key":"X-Off","value":"x","is_checked":0}]},"query":{"parameter":[{"key":"q","value":"{{id}}","is_checked":1}]},"restful":{"parameter":[{"key":"id","value":"42","is_checked":1}]},"body":{"mode":"json","raw":"{\"id\":\"{{id}}\"}"}}}
            ]}
            """;
        var workspace = new WorkspaceDocument();
        var plan = _service.Preview(workspace, json, "target", "api.json");
        _service.Apply(workspace, plan, false);
        var request = Assert.Single(workspace.Requests);
        Assert.Equal("{{baseUrl}}/users/{{id}}", request.Url);
        Assert.Equal("JSON", request.BodyFormat);
        Assert.DoesNotContain("X-Off", request.Headers);
        Assert.Contains("X-Global: yes", request.Headers);
        Assert.Equal("https://example.test", workspace.Environments.Single().Variables.Single(x => x.Key == "baseUrl").Value);
        Assert.True(workspace.SharedVariables.Single(x => x.Key == "token").IsSecret);
        Assert.Equal(workspace.Folders.Single(x => x.Name == "子目录").Id, request.FolderId);
    }
    [Fact]
    public void ApiPostLocalExcludesHistoryResultsAndDeletedTargetsAndUsesNewestRecord()
    {
        const string json = """
            {"apinetLocalFormat":"ApiPost","records":[
             {"project_id":"p","target_id":"r","target_type":"api","name":"old","updated_time":"1","url":"https://example.test/old"},
             {"project_id":"p","target_id":"r","target_type":"api","name":"new","updated_time":"2","url":"https://example.test/new","is_deleted":"0"},
             {"project_id":"p","target_id":"history","target_type":"api","history_id":"h","url":"https://example.test/history"},
             {"project_id":"p","target_id":"removed","target_type":"api","updated_time":"1","url":"https://example.test/old"},
             {"project_id":"p","target_id":"removed","target_type":"api","updated_time":"2","is_deleted":"1"},
             {"result_id":"result","request":{"url":"https://example.test/result"}},
             {"key":"default_server","value":"{\"uri\":\"https://example.test\"}"}
            ]}
            """;
        var plan = _service.Preview(new(), json, null, "local://apipost/test");
        Assert.Equal("new", Assert.Single(plan.Changes).Incoming.Name);
        Assert.Equal("https://example.test/new", plan.Changes[0].Incoming.Url);
        Assert.Equal("baseUrl", Assert.Single(plan.SharedVariables).Key);
    }
    [Fact]
    public void MultipartFilesAreDisabledAndCollectionFormsRoundTripThroughOpenApi()
    {
        const string json = """
            {"info":{"name":"Forms"},"item":[{"id":"r","name":"Upload","request":{"method":"POST","url":"https://example.test/upload","body":{"mode":"formdata","formdata":[{"key":"title","value":"{{title}}","type":"text"},{"key":"file","type":"file","src":"/private/file"}]}}}]}
            """;
        var workspace = new WorkspaceDocument();
        var plan = _service.Preview(workspace, json, null, "form.json");
        var request = Assert.Single(plan.Changes).Incoming;
        Assert.False(request.BodyParameters[1].Enabled);
        Assert.Equal("File", request.BodyParameters[1].Type);
        Assert.Equal("/private/file", request.BodyParameters[1].Value);
        Assert.Contains(plan.Warnings, x => x.Contains("文件上传"));
        _service.Apply(workspace, plan, false);
        var openApi = new OpenApiService();
        var roundtrip = openApi.Preview(new(), openApi.Export(workspace, null), null, "export.json");
        Assert.Equal("Multipart", Assert.Single(roundtrip.Changes).Incoming.BodyFormat);
        Assert.Equal("{{title}}", roundtrip.Changes[0].Incoming.BodyParameters[0].Value);
    }
    [Fact]
    public void EmptyCollectionRefreshMarksPreviousRequestsAsRemoved()
    {
        var workspace = new WorkspaceDocument();
        _service.Apply(workspace, _service.Preview(workspace, Postman, null, "demo.json"), false);
        var empty = JsonNode.Parse(Postman)!; empty["item"] = new JsonArray();
        var plan = _service.Preview(workspace, empty.ToJsonString(), null, "demo.json");
        Assert.Equal(4, plan.Changes.Count);
        Assert.All(plan.Changes, x => Assert.Equal("移除", x.Kind));
        _service.Apply(workspace, plan, false);
        Assert.All(workspace.Requests, x => Assert.True(x.SourceRemoved));
    }
    [Fact]
    public void UnknownFormatsAndCyclicFoldersFailBeforeChangingWorkspace()
    {
        var workspace = new WorkspaceDocument();
        Assert.Throws<FormatException>(() => _service.Preview(workspace, "{\"unrelated\":true}", null, "file"));
        Assert.Throws<FormatException>(() => _service.Preview(workspace, """{"name":"cycle","apis":[{"target_id":"a","target_type":"folder","parent_id":"b"},{"target_id":"b","target_type":"folder","parent_id":"a"}]}""", null, "file"));
        Assert.Empty(workspace.Folders);
        Assert.Empty(workspace.ImportSources);
    }
}
