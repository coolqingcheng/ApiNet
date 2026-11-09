using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;

namespace ApiNet.Android;

public sealed class AndroidImportService(IWorkspaceImportService inner) : IWorkspaceImportService
{
    public Task<IReadOnlyList<LocalImportSource>> DetectLocalAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalImportSource>>([]);
    public Task<string> ReadLocalAsync(LocalImportSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException("Android 请通过 JSON 文件导入。");
    public Task<string> ReadUrlAsync(string url, CancellationToken cancellationToken = default) => inner.ReadUrlAsync(url, cancellationToken);
    public ImportPlan Preview(WorkspaceDocument workspace, string json, string? folderId, string location) => inner.Preview(workspace, json, folderId, location);
    public void Apply(WorkspaceDocument workspace, ImportPlan plan, bool overwriteConflicts) => inner.Apply(workspace, plan, overwriteConflicts);
}
