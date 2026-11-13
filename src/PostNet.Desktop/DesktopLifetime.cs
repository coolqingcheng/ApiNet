using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PostNet.Desktop.ViewModels;
using PostNet.Desktop.Views;
using PostNet.Desktop.Services;
using PostNet.Infrastructure.Persistence;
using PostNet.Infrastructure.Variables;
using PostNet.Infrastructure.Http;
using PostNet.Infrastructure.OpenApi;
using PostNet.Infrastructure.Updates;
using PostNet.Proxy;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Controls;
using PostNet.Platform;

namespace PostNet.Desktop;

public sealed class DesktopLifetime
{
    private bool _exiting;
    private bool _canShutdown;
    private TrayIcon? _tray;
    private DispatcherTimer? _updateTimer;

    public void Initialize(Application application)
    {
        if (application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = new SqliteWorkspaceStore(Environment.GetEnvironmentVariable("APINET_DATA_DIR"));
            WindowsShellIconRefresh.Refresh(store.DataDirectory, AppContext.BaseDirectory);
            var proxy = new ForwardProxyService(store.DataDirectory);
            var updates = new VelopackUpdateService();
            var window = new MainWindow();
            var dialogs = new DialogService(() => window);
            var model = new MainWindowViewModel(store, new VariableResolver(), new HttpRequestService(), new OpenApiService(), proxy, updates, dialogs);
            window.DataContext = model;
            desktop.MainWindow = window;
            void ShowWindow()
            {
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            }
            async Task ExitAsync(bool restart)
            {
                if (_exiting)
                    return;
                _exiting = true;
                try
                {
                    _updateTimer?.Stop();
                    await model.ShutdownAsync();
                    model.ApplyUpdate(restart);
                    await proxy.DisposeAsync();
                    _tray?.Dispose();
                    _canShutdown = true;
                    desktop.Shutdown();
                }
                catch (Exception exception)
                {
                    _exiting = false;
                    ShowWindow();
                    await dialogs.ChooseAsync("退出未完成", exception.Message, ["返回应用"]);
                }
            }
            try
            {
                var menu = new NativeMenu();
                var show = new NativeMenuItem("显示主窗口");
                show.Click += (_, _) => ShowWindow();
                var toggleProxy = new NativeMenuItem("启动 / 停止代理");
                toggleProxy.Click += async (_, _) => await model.ToggleProxyCommand.ExecuteAsync(null);
                var quit = new NativeMenuItem("退出应用");
                quit.Click += async (_, _) => await ExitAsync(false);
                menu.Items.Add(show);
                menu.Items.Add(toggleProxy);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(quit);
                _tray = new TrayIcon { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PostNet.UI/Assets/app.ico"))), ToolTipText = "Post.NET · API 调试工具", Menu = menu, IsVisible = true };
                _tray.Clicked += (_, _) => ShowWindow();
                TrayIcon.SetIcons(application, [_tray]);
            }
            catch (Exception) { _tray = null; }
            window.Closing += async (_, args) =>
            {
                if (_canShutdown)
                    return;
                args.Cancel = true;
                if (model.Workspace.Settings.CloseToTray)
                {
                    if (_tray is null || OperatingSystem.IsLinux())
                        window.WindowState = WindowState.Minimized;
                    else
                        window.Hide();
                }
                else
                    await ExitAsync(false);
            };
            desktop.ShutdownRequested += (_, args) => { if (!_canShutdown) { args.Cancel = true; _ = ExitAsync(false); } };
            model.ExitRequested += restart => _ = ExitAsync(restart);
            window.Opened += async (_, _) =>
            {
                try
                {
                    await model.InitializeAsync();
                }
                catch (Exception exception) { await dialogs.ChooseAsync("无法加载工作区", exception.Message + "\n原数据已保留，请检查数据库。", ["退出"]); _canShutdown = true; _tray?.Dispose(); desktop.Shutdown(1); return; }
                _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
                _updateTimer.Tick += async (_, _) => { if (model.Workspace.Settings.AutoCheckUpdates) await model.CheckUpdatesCommand.ExecuteAsync(null); };
                _updateTimer.Start();
            };
        }


    }
}
