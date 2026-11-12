using ApiNet.Core.Interfaces;
using Velopack;
using Velopack.Sources;
using Velopack.Locators;
using System.Runtime.InteropServices;

namespace ApiNet.Infrastructure.Updates;

public sealed class VelopackUpdateService : IUpdateService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateManager? _manager;
    private UpdateInfo? _update;
    public bool IsReady => _update is not null;
    public event Action<UpdateState>? StateChanged;

    public async Task CheckAndDownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidOperationException("更新源地址无效。");
            StateChanged?.Invoke(new("正在检查更新…", Busy: true));
            if (!VelopackLocator.IsCurrentSet || VelopackLocator.Current.CurrentlyInstalledVersion is null)
            {
                StateChanged?.Invoke(new("开发运行：安装正式包后可在线升级。"));
                return;
            }
            var rid = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
            var manager = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(url.TrimEnd('/'), null, false), new UpdateOptions { ExplicitChannel = rid })
                : new UpdateManager(url.TrimEnd('/'), new UpdateOptions { ExplicitChannel = rid });
            if (!manager.IsInstalled)
            {
                StateChanged?.Invoke(new("当前为开发运行或非 Velopack 安装，安装正式包后可在线升级。"));
                return;
            }
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                StateChanged?.Invoke(new("当前已是最新版本"));
                return;
            }
            var version = update.TargetFullRelease.Version.ToString();
            StateChanged?.Invoke(new("正在下载更新…", 0, version, Busy: true));
            await manager.DownloadUpdatesAsync(update, progress => StateChanged?.Invoke(new("正在下载更新…", progress, version, Busy: true)), cancellationToken);
            _manager = manager;
            _update = update;
            StateChanged?.Invoke(new("更新已下载，可以立即重启升级或退出时升级。", 100, version, true, ReleaseNotes: update.TargetFullRelease.NotesMarkdown));
        }
        catch (Exception exception) { StateChanged?.Invoke(new($"更新失败：{exception.Message}")); }
        finally { _gate.Release(); }
    }

    public void Apply(bool restart)
    {
        if (_manager is null || _update is null)
            return;
        if (restart)
            _manager.ApplyUpdatesAndRestart(_update);
        else
            _manager.ApplyUpdatesAndExit(_update);
    }
}
