using System.Text;
using PostNet.Core.Models;
using PostNet.Desktop.Services.Documents;
using PostNet.Desktop.ViewModels.Workspace;
using PostNet.Infrastructure.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private readonly HashSet<string> _workspaceSelectionIds = new(StringComparer.Ordinal);
    public bool HasWorkspaceSelection => _workspaceSelectionIds.Count > 0;
    public string WorkspaceSelectionSummary => HasWorkspaceSelection ? $"已选择 {_workspaceSelectionIds.Count} 项 · {WorkspaceExportSelection.Create(Workspace, _workspaceSelectionIds).Requests.Count} 个请求" : "按住 Ctrl / ⌘ 点击可多选，文件夹包含所有子项";
    private WorkspaceNodeViewModel TrackWorkspaceSelection(WorkspaceNodeViewModel node)
    {
        node.IsSelected = _workspaceSelectionIds.Contains(node.Id);
        node.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(node.IsSelected)) return;
            if (IsRefreshingWorkspaceTree) return;
            if (node.IsSelected) _workspaceSelectionIds.Add(node.Id); else _workspaceSelectionIds.Remove(node.Id);
            OnPropertyChanged(nameof(WorkspaceSelectionSummary)); OnPropertyChanged(nameof(HasWorkspaceSelection));
        };
        return node;
    }
    [RelayCommand] private void ClearWorkspaceSelection()
    {
        _workspaceSelectionIds.Clear();
        foreach (var node in Flatten(Nodes)) node.IsSelected = false;
        SelectedNode = null;
        OnPropertyChanged(nameof(WorkspaceSelectionSummary)); OnPropertyChanged(nameof(HasWorkspaceSelection));
    }
    public void ClearHiddenWorkspaceSelection()
    {
        _workspaceSelectionIds.IntersectWith(Flatten(Nodes).Select(x => x.Id));
        OnPropertyChanged(nameof(WorkspaceSelectionSummary)); OnPropertyChanged(nameof(HasWorkspaceSelection));
    }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ExportCommand))] private bool _isExporting;
    private bool CanExport() => !IsExporting;
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        IsExporting = true;
        try
        {
            var selectedIds = HasWorkspaceSelection ? _workspaceSelectionIds.ToArray() : SelectedNode is { } node ? [node.Id] : null;
            var includedIds = WorkspaceExportSelection.Create(Workspace, selectedIds).Requests.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var tab in Tabs.Where(x => includedIds.Contains(x.Id)))
                if (!tab.TryCommitParameterEdits()) { Status = tab.Name + "：" + tab.Status; return; }
            var snapshot = new WorkspaceDocument
            {
                Folders = Workspace.Folders, ImportSources = Workspace.ImportSources,
                SharedVariables = Workspace.SharedVariables, Environments = Workspace.Environments,
                SelectedEnvironmentId = Workspace.SelectedEnvironmentId,
                Requests = Workspace.Requests.Select(x =>
                {
                    var draft = Tabs.FirstOrDefault(tab => tab.Id == x.Id)?.Snapshot();
                    if (draft is null) return x;
                    draft.FolderId = x.FolderId;
                    return draft;
                }).ToList()
            };
            var selected = WorkspaceExportSelection.Create(snapshot, selectedIds);
            if (selected.Requests.Count == 0) { Status = "所选范围没有可导出的请求"; return; }
            var scope = HasWorkspaceSelection ? string.Join("\n", _workspaceSelectionIds.Take(8).Select(id => "• " + (Workspace.Folders.FirstOrDefault(x => x.Id == id)?.Name ?? Workspace.Requests.FirstOrDefault(x => x.Id == id)?.Name))) : SelectedNode?.Name ?? "整个工作区";
            var choice = await _dialogs.ChooseAsync("导出接口文档", scope + $"\n\n包含 {selected.Requests.Count} 个请求（文件夹递归包含所有子项，重叠选择自动去重）。\n使用当前编辑内容，变量保留为占位符。", ["Markdown (.md)", "PDF (.pdf)", "OpenAPI JSON (.json)", "取消"]);
            if (choice is < 0 or > 2) { Status = "已取消导出"; return; }
            Status = "正在生成接口文档…";
            var content = await Task.Run(() => choice switch
            {
                0 => Encoding.UTF8.GetBytes(RequestDocumentBuilder.Markdown(RequestDocumentBuilder.Build(selected))),
                1 => PdfDocumentWriter.Write(RequestDocumentBuilder.Build(selected)),
                _ => Encoding.UTF8.GetBytes(_openApi.Export(selected, null))
            });
            var name = choice switch { 0 => "api-document.md", 1 => "api-document.pdf", _ => "openapi.json" };
            Status = await _dialogs.WriteDocumentAsync(name, content) is { } path ? $"已导出 {selected.Requests.Count} 个请求 · {path}" : "已取消导出";
        }
        catch (Exception exception) { Status = "导出失败：" + exception.Message; }
        finally { IsExporting = false; }
    }
}
