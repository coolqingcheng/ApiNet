using PostNet.Core.Models;
using PostNet.Core.Serialization;

namespace PostNet.Infrastructure.Documents;

public static class WorkspaceExportSelection
{
    // null selects the workspace; an explicitly empty selection selects nothing.
    public static WorkspaceDocument Create(WorkspaceDocument workspace, IEnumerable<string>? selectedIds)
    {
        var selected = selectedIds?.ToHashSet(StringComparer.Ordinal);
        var folders = workspace.Folders.Where(x => selected is null || selected.Contains(x.Id)).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var folder in workspace.Folders)
                if (folder.ParentId is not null && folders.Contains(folder.ParentId)) changed |= folders.Add(folder.Id);
        } while (changed);
        var requests = workspace.Requests.Where(x => selected is null || selected.Contains(x.Id) || x.FolderId is not null && folders.Contains(x.FolderId))
            .DistinctBy(x => x.Id).Select(ModelCopy.Request).ToList();
        // Keep the ancestors of selected requests and folders so exported paths round-trip.
        var ancestors = folders.Concat(requests.Select(x => x.FolderId).OfType<string>()).ToHashSet(StringComparer.Ordinal);
        do
        {
            changed = false;
            foreach (var folder in workspace.Folders)
                if (ancestors.Contains(folder.Id) && folder.ParentId is not null) changed |= ancestors.Add(folder.ParentId);
        } while (changed);
        return new WorkspaceDocument
        {
            Requests = requests,
            Folders = workspace.Folders.Where(x => ancestors.Contains(x.Id)).Select(x => new FolderDefinition { Id = x.Id, ParentId = x.ParentId, Name = x.Name, Order = x.Order }).ToList(),
            ImportSources = workspace.ImportSources.Where(x => requests.Any(r => r.SourceId == x.Id)).Select(x => new ImportSource
            {
                Id = x.Id, FolderId = x.FolderId, Format = x.Format, Location = x.Location,
                DocumentJson = x.DocumentJson, BaseUrlVariable = x.BaseUrlVariable
            }).ToList(),
            SharedVariables = workspace.SharedVariables.Select(CopyVariable).ToList(),
            Environments = workspace.Environments.Select(x => new EnvironmentDefinition
            {
                Id = x.Id, Name = x.Name, Variables = x.Variables.Select(CopyVariable).ToList()
            }).ToList(),
            SelectedEnvironmentId = workspace.SelectedEnvironmentId
        };
    }

    private static VariableDefinition CopyVariable(VariableDefinition value) => new() { Key = value.Key, Value = value.Value, IsSecret = value.IsSecret };
}
