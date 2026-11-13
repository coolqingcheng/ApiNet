namespace PostNet.Core.Models;

public sealed class ImportPlan
{
    public string Format { get; set; } = "OpenAPI";
    public List<string> Warnings { get; set; } = [];
    public List<VariableDefinition> SharedVariables { get; set; } = [];
    public List<EnvironmentDefinition> Environments { get; set; } = [];
    public ImportSource Source { get; set; } = new();
    public List<ImportChange> Changes { get; set; } = [];
    public List<FolderDefinition> NewFolders { get; set; } = [];
    public string? BaseUrl
    {
        get; set;
    }
    public string Summary => $"新增 {Changes.Count(x => x.Kind == "新增")} · 更新 {Changes.Count(x => x.Kind == "更新")} · 冲突 {Changes.Count(x => x.Kind == "冲突")} · 来源移除 {Changes.Count(x => x.Kind == "移除")}";
}

public sealed class ImportChange
{
    public string Kind { get; set; } = "新增";
    public RequestDefinition Incoming { get; set; } = new();
    public string? ExistingId
    {
        get; set;
    }
    public List<string> ConflictFields { get; set; } = [];
    public string Display => $"{Kind}  {Incoming.Method} {Incoming.Url}  {Incoming.Name}";
}
