namespace ApiNet.Core.Models;

public sealed class WorkspaceDocument
{
    public int Version { get; set; } = 1;
    public List<FolderDefinition> Folders { get; set; } = [];
    public List<RequestDefinition> Requests { get; set; } = [];
    public List<EnvironmentDefinition> Environments { get; set; } = [];
    public List<VariableDefinition> SharedVariables { get; set; } = [];
    public List<ImportSource> ImportSources { get; set; } = [];
    public List<RequestDefinition> OpenTabs { get; set; } = [];
    public string? SelectedEnvironmentId
    {
        get; set;
    }
    public AppSettings Settings { get; set; } = new();
}

public sealed class FolderDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId
    {
        get; set;
    }
    public string Name { get; set; } = "新建文件夹";
    public int Order
    {
        get; set;
    }
}

public sealed class EnvironmentDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新环境";
    public List<VariableDefinition> Variables { get; set; } = [];
}

public sealed class VariableDefinition
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsSecret
    {
        get; set;
    }
}

public sealed class AppSettings
{
    public bool CloseToTray
    {
        get; set;
    }
    public int ProxyPort { get; set; } = 8877;
    public bool DecryptHttps
    {
        get; set;
    }
    public string UpdateUrl { get; set; } = "https://github.com/coolqingcheng/ApiNet";
    public bool AutoCheckUpdates { get; set; } = true;
}

public sealed class ImportSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? FolderId
    {
        get; set;
    }
    public string Location { get; set; } = "";
    public string DocumentJson { get; set; } = "{}";
    public string BaseUrlVariable { get; set; } = "baseUrl";
}
