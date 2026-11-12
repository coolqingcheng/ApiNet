using ApiNet.Core.Models;

namespace ApiNet.Core.Interfaces;

public interface IWorkspaceStore
{
    Task<WorkspaceDocument?> LoadAsync();
    Task SaveAsync(WorkspaceDocument document);
    Task AddHistoryAsync(HistoryEntry entry);
    Task<List<HistoryEntry>> GetHistoryAsync();
    Task<HistoryEntry?> GetHistoryDetailAsync(string id);
    Task ClearHistoryAsync();
    string DataDirectory
    {
        get;
    }
}

public interface IVariableResolver
{
    string Resolve(string text, WorkspaceDocument workspace);
    RequestDefinition ResolveRequest(RequestDefinition request, WorkspaceDocument workspace);
}

public interface IHttpRequestService
{
    Task<HttpResult> SendAsync(RequestDefinition request, CancellationToken cancellationToken);
}

public interface IWebSocketSession : IAsyncDisposable
{
    event Action<SocketMessage>? MessageReceived;
    bool IsConnected
    {
        get;
    }
    Task ConnectAsync(RequestDefinition request, CancellationToken cancellationToken);
    Task SendAsync(string content, bool binary, CancellationToken cancellationToken);
    Task DisconnectAsync();
}

public interface IOpenApiService
{
    ImportPlan Preview(WorkspaceDocument workspace, string json, string? folderId, string location);
    void Apply(WorkspaceDocument workspace, ImportPlan plan, bool overwriteConflicts);
    string Export(WorkspaceDocument workspace, string? folderId);
}

public sealed record LocalImportSource(string Id, string Format, string Name, string DataDirectory, string? ExecutablePath = null, string? BackupPath = null)
{
    public string Location => "local://" + Format.ToLowerInvariant() + "/" + Uri.EscapeDataString(Path.GetFullPath(DataDirectory));
    public string Description => Name + (ExecutablePath is null ? " · 本地数据" : " · 已安装") + (BackupPath is null ? " · 数据库" : " · 含自动备份");
}

public interface IWorkspaceImportService
{
    ImportPlan Preview(WorkspaceDocument workspace, string json, string? folderId, string location);
    void Apply(WorkspaceDocument workspace, ImportPlan plan, bool overwriteConflicts);
    Task<IReadOnlyList<LocalImportSource>> DetectLocalAsync(CancellationToken cancellationToken = default);
    Task<string> ReadLocalAsync(LocalImportSource source, CancellationToken cancellationToken = default);
    Task<string> ReadUrlAsync(string url, CancellationToken cancellationToken = default);
}

public interface IProxyService : IAsyncDisposable
{
    bool IsRunning
    {
        get;
    }
    event Action<CaptureRecord>? Captured;
    Task StartAsync(int port, bool decryptHttps);
    Task StopAsync();
    string ExportCertificate();
}

public sealed record UpdateState(string Status, double Progress = 0, string? Version = null, bool Ready = false, bool Busy = false, string? ReleaseNotes = null);

public interface IUpdateService
{
    event Action<UpdateState>? StateChanged;
    bool IsReady
    {
        get;
    }
    Task CheckAndDownloadAsync(string url, CancellationToken cancellationToken = default);
    void Apply(bool restart);
}
