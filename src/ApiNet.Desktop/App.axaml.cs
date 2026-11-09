using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.Views;
using ApiNet.Desktop.Services;
using ApiNet.Infrastructure.Persistence;
using ApiNet.Infrastructure.Variables;
using ApiNet.Infrastructure.Http;
using ApiNet.Infrastructure.OpenApi;
using ApiNet.Infrastructure.Updates;
using ApiNet.Proxy;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Controls;

namespace ApiNet.Desktop;

public partial class App : Application
{
    private bool _exiting;
    private bool _canShutdown;
    private TrayIcon? _tray;
    private DispatcherTimer? _updateTimer;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = new SqliteWorkspaceStore(Environment.GetEnvironmentVariable("APINET_DATA_DIR"));
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
                _tray = new TrayIcon { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ApiNet.Desktop/Assets/app.ico"))), ToolTipText = "ApiNet · 网络调试工作台", Menu = menu, IsVisible = true };
                _tray.Clicked += (_, _) => ShowWindow();
                TrayIcon.SetIcons(this, [_tray]);
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

        base.OnFrameworkInitializationCompleted();
    }
}
