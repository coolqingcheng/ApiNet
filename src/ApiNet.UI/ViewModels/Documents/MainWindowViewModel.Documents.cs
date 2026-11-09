using System.Text;
using ApiNet.Core.Models;
using ApiNet.Desktop.Services.Documents;
using ApiNet.Desktop.ViewModels.Workspace;
using ApiNet.Infrastructure.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private readonly HashSet<string> _exportSelectionIds = new(StringComparer.Ordinal);
    public bool HasExportSelection => _exportSelectionIds.Count > 0;
    public string ExportSelectionSummary => HasExportSelection ? $"已勾选 {_exportSelectionIds.Count} 项 · {WorkspaceExportSelection.Create(Workspace, _exportSelectionIds).Requests.Count} 个请求" : "勾选条目可多选导出，文件夹包含所有子项";
    private WorkspaceNodeViewModel TrackExportSelection(WorkspaceNodeViewModel node)
    {
        node.IsExportSelected = _exportSelectionIds.Contains(node.Id);
        node.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(node.IsExportSelected)) return;
            if (node.IsExportSelected) _exportSelectionIds.Add(node.Id); else _exportSelectionIds.Remove(node.Id);
            OnPropertyChanged(nameof(ExportSelectionSummary)); OnPropertyChanged(nameof(HasExportSelection));
        };
        return node;
    }
    [RelayCommand] private void ClearExportSelection()
    {
        _exportSelectionIds.Clear();
        foreach (var node in Flatten(Nodes)) node.IsExportSelected = false;
        OnPropertyChanged(nameof(ExportSelectionSummary)); OnPropertyChanged(nameof(HasExportSelection));
    }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ExportCommand))] private bool _isExporting;
    private bool CanExport() => !IsExporting;
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        IsExporting = true;
        try
        {
            var selectedIds = HasExportSelection ? _exportSelectionIds.ToArray() : SelectedNode is { } node ? [node.Id] : null;
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
            var scope = HasExportSelection ? string.Join("\n", _exportSelectionIds.Take(8).Select(id => "• " + (Workspace.Folders.FirstOrDefault(x => x.Id == id)?.Name ?? Workspace.Requests.FirstOrDefault(x => x.Id == id)?.Name))) : SelectedNode?.Name ?? "整个工作区";
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
