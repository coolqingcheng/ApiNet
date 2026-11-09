using System.Text.Json.Nodes;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Imports.Local;

namespace ApiNet.Infrastructure.Imports;

public sealed class WorkspaceImportService(IOpenApiService openApi, LocalImportReader? local = null) : IWorkspaceImportService
{
    private readonly LocalImportReader _local = local ?? new();
    public ImportPlan Preview(WorkspaceDocument workspace, string json, string? folderId, string location)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("请提供 OpenAPI、Postman 或 ApiPost JSON 对象。");
        if (root["collection"] is JsonObject collection && collection["item"] is JsonArray) root = collection;
        if (root["openapi"] is not null || root["swagger"] is not null) return openApi.Preview(workspace, json, folderId, location);
        var format = root["apinetLocalFormat"]?.GetValue<string>();
        if (format is null)
        {
            if (root["item"] is JsonArray || root["collections"] is JsonArray || root["_postman_variable_scope"] is not null || root["values"] is JsonArray && root["name"] is not null) format = "Postman";
            else if (root["apis"] is JsonArray || root["target_type"] is not null || root["data"] is JsonObject data && data["apis"] is JsonArray) format = "ApiPost";
        }
        if (format is not ("Postman" or "ApiPost")) throw new FormatException("未识别的格式。支持 OpenAPI/Swagger、Postman Collection/环境/备份以及 ApiPost 项目 JSON。");
        var previous = workspace.ImportSources.FirstOrDefault(x => x.FolderId == folderId && x.Location == location);
        var plan = new ImportPlan { Format = format, Source = new ImportSource { Id = previous?.Id ?? Guid.NewGuid().ToString("N"), Format = format, FolderId = folderId, Location = location, DocumentJson = json } };
        var context = new ImportContext(workspace, plan);
        if (format == "Postman") PostmanImporter.Parse(context, root, folderId);
        else ApiPostImporter.Parse(context, root, folderId);
        foreach (var warning in (root["apinetWarnings"] as JsonArray ?? []).Select(x => x?.GetValue<string>()).OfType<string>()) context.Warn(warning);
        if (context.Requests.Count == 0 && plan.Environments.Count == 0 && plan.SharedVariables.Count == 0 && plan.NewFolders.Count == 0 && previous is null)
            throw new FormatException("未发现可导入的请求或变量。本地尚未同步、加密或云端专有数据需先在原软件中导出 JSON。");
        foreach (var variable in plan.SharedVariables)
            if (workspace.SharedVariables.Any(x => x.Key == variable.Key && x.Value != variable.Value)) context.Warn("已有公共变量将保留本地值：" + variable.Key);
        foreach (var env in plan.Environments)
            if (workspace.Environments.FirstOrDefault(x => x.Id == env.Id) is { } old && env.Variables.Any(v => old.Variables.Any(x => x.Key == v.Key && x.Value != v.Value))) context.Warn("已有环境变量将保留本地值：" + env.Name);
        RequestImportMerger.Preview(workspace, plan, context.Requests);
        return plan;
    }
    public void Apply(WorkspaceDocument workspace, ImportPlan plan, bool overwriteConflicts)
    {
        if (plan.Source.Format == "OpenAPI") { openApi.Apply(workspace, plan, overwriteConflicts); return; }
        RequestImportMerger.Apply(workspace, plan, overwriteConflicts);
        foreach (var variable in plan.SharedVariables)
            if (workspace.SharedVariables.All(x => x.Key != variable.Key)) workspace.SharedVariables.Add(variable);
        foreach (var environment in plan.Environments)
        {
            var old = workspace.Environments.FirstOrDefault(x => x.Id == environment.Id);
            if (old is null) workspace.Environments.Add(environment);
            else foreach (var variable in environment.Variables)
                if (old.Variables.All(x => x.Key != variable.Key)) old.Variables.Add(variable);
        }
        workspace.ImportSources.RemoveAll(x => x.Id == plan.Source.Id);
        workspace.ImportSources.Add(plan.Source);
    }
    public Task<IReadOnlyList<LocalImportSource>> DetectLocalAsync(CancellationToken cancellationToken = default) => _local.DetectAsync(cancellationToken);
    public Task<string> ReadLocalAsync(LocalImportSource source, CancellationToken cancellationToken = default) => _local.ReadAsync(source, cancellationToken);
    public Task<string> ReadUrlAsync(string url, CancellationToken cancellationToken = default) => new OpenApiDocumentDownloader().ReadAsync(url, cancellationToken);
}
