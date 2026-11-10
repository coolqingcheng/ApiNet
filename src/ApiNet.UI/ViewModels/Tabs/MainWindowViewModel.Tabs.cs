using ApiNet.Desktop.ViewModels.Tabs;

namespace ApiNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    internal bool IsClosingTabs { get; private set; }

    internal void RefreshTabCloseCommands()
    {
        foreach (var tab in Tabs) tab.RefreshCloseCommands();
    }

    public async Task<bool> CloseTabAsync(RequestTabViewModel tab)
    {
        if (IsClosingTabs || !Tabs.Contains(tab)) return false;
        IsClosingTabs = true;
        RefreshTabCloseCommands();
        try { return await CloseTabCoreAsync(tab); }
        finally { IsClosingTabs = false; RefreshTabCloseCommands(); }
    }

    public Task CloseOtherTabsAsync(RequestTabViewModel anchor) => Tabs.Contains(anchor)
        ? CloseTabsAsync(Tabs.Where(tab => tab != anchor).ToArray()) : Task.CompletedTask;

    public Task CloseTabsToLeftAsync(RequestTabViewModel anchor) => Tabs.Contains(anchor)
        ? CloseTabsAsync(Tabs.Take(Tabs.IndexOf(anchor)).ToArray()) : Task.CompletedTask;

    public Task CloseTabsToRightAsync(RequestTabViewModel anchor) => Tabs.Contains(anchor)
        ? CloseTabsAsync(Tabs.Skip(Tabs.IndexOf(anchor) + 1).ToArray()) : Task.CompletedTask;

    public Task CloseAllTabsAsync() => CloseTabsAsync(Tabs.ToArray());

    private async Task CloseTabsAsync(IReadOnlyList<RequestTabViewModel> tabs)
    {
        if (IsClosingTabs) return;
        IsClosingTabs = true;
        RefreshTabCloseCommands();
        try
        {
            foreach (var tab in tabs)
                if (Tabs.Contains(tab) && !await CloseTabCoreAsync(tab)) break;
        }
        finally { IsClosingTabs = false; RefreshTabCloseCommands(); }
    }

    private async Task<bool> CloseTabCoreAsync(RequestTabViewModel tab)
    {
        if (tab is WebSocketTabViewModel { IsConnected: true } && !await _dialogs.ConfirmAsync("断开连接", "关闭此标签页将断开 WebSocket，是否继续？"))
            return false;
        if (tab.IsDirty)
        {
            var action = await _dialogs.ChooseAsync("未保存的修改", $"“{tab.Name}”已修改。", ["保存", "放弃修改", "取消"]);
            if (action is < 0 or 2) return false;
            if (action == 0)
            {
                await SaveTabAsync(tab);
                if (tab.IsDirty) return false;
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
}
