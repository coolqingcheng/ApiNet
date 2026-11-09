using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private readonly HashSet<string> _announcedUpdates = [];
    private string? _downloadedVersion;
    private bool _updatePromptOpen;

    private void OnUpdateStateChanged(UpdateState state) => Dispatcher.UIThread.Post(() =>
    {
        UpdateStatus = state.Version is null ? state.Status : $"v{state.Version} · {state.Status}";
        UpdateProgress = state.Progress;
        UpdateReady = state.Ready;
        UpdateBusy = state.Busy;
        if (state.Ready)
        {
            _downloadedVersion = state.Version;
            if (_announcedUpdates.Add(state.Version ?? "ready")) _ = ShowUpdatePromptAsync();
        }
    });

    [RelayCommand] private Task CheckUpdatesAsync() => _updates.CheckAndDownloadAsync(AppSettings.UpdateRepository);

    [RelayCommand]
    private async Task ShowUpdatePromptAsync()
    {
        if (!_updates.IsReady || _updatePromptOpen) return;
        _updatePromptOpen = true;
        try
        {
            var title = _downloadedVersion is null ? "发现新版本" : "发现新版本 " + _downloadedVersion;
            var choice = await _dialogs.ChooseAsync(title, "更新包已下载完成。可以立即重启升级，或在退出应用时安装。", ["立即升级", "退出时升级", "稍后"]);
            if (choice == 0) UpgradeNow();
            else if (choice == 1) UpgradeOnExit();
        }
        catch (Exception exception) { UpdateStatus = "更新已就绪：" + exception.Message; }
        finally { _updatePromptOpen = false; }
    }
    [RelayCommand] private void UpgradeNow() { if (_updates.IsReady) ExitRequested?.Invoke(true); }
    [RelayCommand]
    private void UpgradeOnExit()
    {
        UpdateOnExit = true;
        UpdateStatus = "将在真正退出应用时安装更新；隐藏到托盘会继续运行。";
    }
}
