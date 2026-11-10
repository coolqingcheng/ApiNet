using System.Collections.ObjectModel;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Core.Serialization;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels.Tabs;
using ApiNet.Desktop.ViewModels.Workspace;
using ApiNet.Infrastructure.WebSockets;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private string? TargetFolder => SelectedNode?.IsFolder == true ? SelectedNode.Id : Workspace.Requests.FirstOrDefault(x => x.Id == SelectedNode?.Id)?.FolderId;
    public bool IsRefreshingWorkspaceTree { get; private set; }
    private void RefreshTree()
    {
        IsRefreshingWorkspaceTree = true;
        try
        {
            var selectedId = SelectedNode?.Id;
            _workspaceSelectionIds.RemoveWhere(id => Workspace.Folders.All(x => x.Id != id) && Workspace.Requests.All(x => x.Id != id));
            var expanded = Flatten(Nodes).ToDictionary(x => x.Id, x => x.IsExpanded);
            var visited = new HashSet<string>();
            List<WorkspaceNodeViewModel> Build(string? parent)
            {
                List<WorkspaceNodeViewModel> nodes = [];
                foreach (var folder in Workspace.Folders.Where(x => x.ParentId == parent).OrderBy(x => x.Order))
                {
                    if (!visited.Add(folder.Id))
                        continue;
                    var node = TrackWorkspaceSelection(new WorkspaceNodeViewModel(folder) { IsExpanded = Search.Length > 0 || !expanded.TryGetValue(folder.Id, out var value) || value });
                    foreach (var child in Build(folder.Id))
                        node.Children.Add(child);
                    if (Search.Length == 0 || node.Children.Count > 0 || folder.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
                        nodes.Add(node);
                }
                nodes.AddRange(Workspace.Requests.Where(x => x.FolderId == parent && (Search.Length == 0 || (x.Name + x.Url + x.Method).Contains(Search, StringComparison.OrdinalIgnoreCase))).Select(x => TrackWorkspaceSelection(new WorkspaceNodeViewModel(x))));
                return nodes;
            }
            Nodes.Clear();
            foreach (var node in Build(null))
                Nodes.Add(node);
            SelectedNode = Flatten(Nodes).FirstOrDefault(x => x.Id == selectedId);
            foreach (var node in Flatten(Nodes)) node.IsSelected = _workspaceSelectionIds.Contains(node.Id);
        }
        finally { IsRefreshingWorkspaceTree = false; }
        OnPropertyChanged(nameof(WorkspaceSelectionSummary));
        OnPropertyChanged(nameof(HasWorkspaceSelection));
    }
    private static IEnumerable<WorkspaceNodeViewModel> Flatten(IEnumerable<WorkspaceNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }

    [RelayCommand] private void Navigate(string panel) => ActivePanel = panel;
    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedNode is { IsFolder: false } node && Workspace.Requests.FirstOrDefault(x => x.Id == node.Id) is { } request)
            OpenRequest(request);
    }
    private RequestTabViewModel CreateTab(RequestDefinition request) => request.Protocol == "WebSocket" ? new WebSocketTabViewModel(request, this, new WebSocketSession()) : new HttpRequestTabViewModel(request, this, _http);
    public void OpenRequest(RequestDefinition request)
    {
        var tab = Tabs.FirstOrDefault(x => x.Id == request.Id);
        if (tab is null)
        {
            tab = CreateTab(request);
            Tabs.Add(tab);
        }
        SelectedTab = tab;
    }
    [RelayCommand] private async Task NewHttpAsync() => await NewRequestAsync(false);
    [RelayCommand] private async Task NewSocketAsync() => await NewRequestAsync(true);
    private async Task NewRequestAsync(bool socket)
    {
        var request = new RequestDefinition { FolderId = TargetFolder, Name = socket ? "新建 WebSocket" : "新建请求", Protocol = socket ? "WebSocket" : "HTTP", Method = socket ? "WS" : "GET", Url = socket ? "{{wsBaseUrl}}/" : "{{baseUrl}}/" };
        Workspace.Requests.Add(request);
        RefreshTree();
        OpenRequest(request);
        await PersistSafelyAsync();
    }
    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var name = await _dialogs.PromptAsync("新建文件夹", "新建文件夹");
        if (string.IsNullOrWhiteSpace(name))
            return;
        var folder = new FolderDefinition { Name = name.Trim(), ParentId = TargetFolder };
        Workspace.Folders.Add(folder);
        RefreshTree();
        SelectedNode = Flatten(Nodes).FirstOrDefault(x => x.Id == folder.Id);
        await PersistSafelyAsync();
    }
    [RelayCommand] private void SelectRoot()
    {
        SelectedNode = null;
        ClearWorkspaceSelection();
    }
    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedNode is not { } node)
            return;
        var name = await _dialogs.PromptAsync("重命名", node.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (node.IsFolder)
            Workspace.Folders.First(x => x.Id == node.Id).Name = name.Trim();
        else
        {
            Workspace.Requests.First(x => x.Id == node.Id).Name = name.Trim();
            var tab = Tabs.FirstOrDefault(x => x.Id == node.Id);
            if (tab is not null)
                tab.Name = name.Trim();
        }
        RefreshTree();
        await PersistSafelyAsync();
    }
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedNode is not { } node || !await _dialogs.ConfirmAsync("删除", $"删除“{node.Name}”及其内容？历史记录仍会保留。"))
            return;
        var ids = new HashSet<string> { node.Id };
        bool changed;
        do
        {
            changed = false;
            foreach (var folder in Workspace.Folders)
                if (folder.ParentId is not null && ids.Contains(folder.ParentId))
                    changed |= ids.Add(folder.Id);
        } while (changed);
        var requests = Workspace.Requests.Where(x => ids.Contains(x.Id) || (x.FolderId is not null && ids.Contains(x.FolderId))).ToArray();
        foreach (var request in requests)
            if (Tabs.FirstOrDefault(x => x.Id == request.Id) is { } tab && !await CloseTabAsync(tab))
                return;
        Workspace.Requests.RemoveAll(x => requests.Contains(x));
        Workspace.Folders.RemoveAll(x => ids.Contains(x.Id));
        Workspace.ImportSources.RemoveAll(x => x.FolderId is not null && ids.Contains(x.FolderId));
        RefreshTree();
        await PersistSafelyAsync();
    }

    public async Task MoveNodeAsync(string id, string? targetFolder)
    {
        if (id == targetFolder)
            return;
        var folder = Workspace.Folders.FirstOrDefault(x => x.Id == id);
        if (folder is not null)
        {
            var ancestor = targetFolder;
            while (ancestor is not null)
            {
                if (ancestor == id)
                    return;
                ancestor = Workspace.Folders.FirstOrDefault(x => x.Id == ancestor)?.ParentId;
            }
            folder.ParentId = targetFolder;
        }
        else if (Workspace.Requests.FirstOrDefault(x => x.Id == id) is { } request)
            request.FolderId = targetFolder;
        RefreshTree();
        await PersistSafelyAsync();
    }

    public async Task SaveTabAsync(RequestTabViewModel tab)
    {
        if (!tab.TryCommitParameterEdits()) { Status = tab.Status; return; }
        var request = tab.Snapshot();
        var existing = Workspace.Requests.FindIndex(x => x.Id == request.Id);
        if (existing < 0)
            Workspace.Requests.Add(request);
        else
        {
            request.FolderId = Workspace.Requests[existing].FolderId;
            Workspace.Requests[existing] = request;
        }
        try
        {
            await PersistAsync();
            tab.MarkSaved(request);
            RefreshTree();
            Status = "请求已保存";
        }
        catch (Exception exception) { Status = "保存失败：" + exception.Message; }
    }
    public async Task DuplicateTabAsync(RequestTabViewModel tab)
    {
        if (!tab.TryCommitParameterEdits()) { Status = tab.Status; return; }
        var request = tab.Snapshot();
        request.Id = Guid.NewGuid().ToString("N");
        request.Name += " 副本";
        request.SourceId = null;
        request.SourceKey = null;
        request.ImportBaselineJson = null;
        Workspace.Requests.Add(request);
        RefreshTree();
        OpenRequest(request);
        await PersistSafelyAsync();
    }
    public async Task<bool> CloseTabAsync(RequestTabViewModel tab)
    {
        if (tab is WebSocketTabViewModel { IsConnected: true } && !await _dialogs.ConfirmAsync("断开连接", "关闭此标签页将断开 WebSocket，是否继续？"))
            return false;
        if (tab.IsDirty)
        {
            var action = await _dialogs.ChooseAsync("未保存的修改", $"“{tab.Name}”已修改。", ["保存", "放弃修改", "取消"]);
            if (action is < 0 or 2)
                return false;
            if (action == 0)
            {
                await SaveTabAsync(tab);
                if (tab.IsDirty)
                    return false;
            }
        }
        await tab.DisposeAsync();
        tab.DisposeParameterTables();
        var index = Tabs.IndexOf(tab);
        var selected = SelectedTab == tab;
        Tabs.Remove(tab);
        if (selected || SelectedTab is null)
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        await PersistSafelyAsync();
        return true;
    }
    [RelayCommand]
    private async Task CloseOtherTabsAsync()
    {
        foreach (var tab in Tabs.Where(x => x != SelectedTab).ToArray())
            if (!await CloseTabAsync(tab))
                break;
    }
    [RelayCommand]
    private async Task CloseAllTabsAsync()
    {
        foreach (var tab in Tabs.ToArray())
            if (!await CloseTabAsync(tab))
                break;
    }

}
