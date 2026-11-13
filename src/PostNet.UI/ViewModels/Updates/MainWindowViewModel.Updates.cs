using PostNet.Core.Interfaces;
using PostNet.Core.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private readonly HashSet<string> _announcedUpdates = [];
    private string? _downloadedVersion;
    private string? _downloadedReleaseNotes;
    private bool _updatePromptOpen;

    private void OnUpdateStateChanged(UpdateState state) => Dispatcher.UIThread.Post(() =>
    {
        UpdateStatus = state.Version is null ? state.Status : $"v{state.Version} · {state.Status}";
        UpdateProgress = state.Progress;
        UpdateReady = state.Ready || _updates.IsReady;
        UpdateBusy = state.Busy;
        if (state.Ready)
        {
            _downloadedVersion = state.Version;
            _downloadedReleaseNotes = state.ReleaseNotes;
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
            var introduction = IsMobile ? "APK 更新已下载并校验。点击安装后由 Android 系统确认覆盖升级。" : "更新包已下载完成。可以立即重启升级，或在退出应用时安装。";
            var notes = string.IsNullOrWhiteSpace(_downloadedReleaseNotes) ? "此版本未提供更新说明。" : _downloadedReleaseNotes.Trim();
            var choice = await _dialogs.ChooseAsync(title, introduction + "\n\n更新内容\n" + notes, IsMobile ? ["安装更新", "稍后"] : ["立即升级", "退出时升级", "稍后"]);
            if (choice == 0) UpgradeNow();
            else if (choice == 1 && !IsMobile) UpgradeOnExit();
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
