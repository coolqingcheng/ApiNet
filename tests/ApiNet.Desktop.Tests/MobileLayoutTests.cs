using ApiNet.Core.Models;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Mobile;
using ApiNet.Desktop.Views.Mobile;
using ApiNet.Infrastructure.Http;
using ApiNet.Infrastructure.OpenApi;
using ApiNet.Infrastructure.Persistence;
using ApiNet.Infrastructure.Updates;
using ApiNet.Infrastructure.Variables;
using ApiNet.Proxy;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiNet.Desktop.Controls;

namespace ApiNet.Desktop.Tests;

public class MobileLayoutTests
{
    [AvaloniaFact]
    public async Task PhoneDrawerRequestsToolsAndTabletLayoutsRemainUsable()
    {
        var path = Path.Combine(Path.GetTempPath(), "apinet-mobile-" + Guid.NewGuid().ToString("N"));
        var view = new MobileMainView();
        var main = new MainWindowViewModel(new SqliteWorkspaceStore(path), new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(path), new VelopackUpdateService(), new MobileDialogService(() => view, path)) { IsMobile = true };
        await main.InitializeAsync();
        var shell = new MobileShellViewModel(main); view.DataContext = shell;
        var window = new Window { Content = view, Width = 390, Height = 844 }; window.Show();
        try
        {
            main.SelectedTab!.Url = "https://example.test/search?term={{baseUrl}}&page=1";
            Save(window, "android-phone-dark.png");
            Assert.True(shell.IsCompact); Assert.False(shell.SidebarVisible);
            Assert.InRange(view.Bounds.Width, 389, 391);
            Assert.Contains(view.GetVisualDescendants().OfType<CodeEditor>(), x => x.Text == main.SelectedTab.Url && x.Bounds.Width < 390 && x.Bounds.Width > 200);
            shell.ToggleDrawerCommand.Execute(null); Save(window, "android-drawer.png"); Assert.True(shell.ShowShade);
            shell.NavigateCommand.Execute("工具"); main.Tools.Input = "{\"message\":\"你好 Android\",\"ok\":true}"; main.Tools.TransformCommand.Execute("format");
            Save(window, "android-tools.png"); Assert.False(shell.DrawerOpen); Assert.True(main.IsTools);
            var output = view.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "OutputPanel"); Assert.Equal(2, Grid.GetRow(output));
            await main.ToggleThemeCommand.ExecuteAsync(null); shell.NavigateCommand.Execute("工作区");
            Save(window, "android-phone-light.png");
            window.Width = 1024; window.Height = 768; Save(window, "android-tablet.png");
            Assert.False(shell.IsCompact); Assert.True(shell.SidebarVisible); Assert.Equal(330, shell.ContentMargin.Left);
            window.Width = 800; window.Height = 400; Save(window, "android-landscape.png"); Assert.True(shell.IsCompact);
            main.OpenRequest(main.Workspace.Requests.Single(x => x.Protocol == "WebSocket"));
            main.SelectedTab!.Body = "{\"message\":\"你好 Android\"}";
            window.Width = 390; window.Height = 844; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var socket = view.GetVisualDescendants().OfType<MobileWebSocketView>().Single();
            socket.FindControl<TabControl>("SocketTabs")!.SelectedIndex = 1;
            Save(window, "android-websocket.png");
            Assert.InRange(socket.FindControl<CodeEditor>("MessageEditor")!.Bounds.Width, 250, 390);
            window.Width = 800; window.Height = 400; Save(window, "android-websocket-landscape.png");
            Assert.True(socket.FindControl<CodeEditor>("MessageEditor")!.Bounds.Height >= 50, "Landscape editor bounds: " + socket.FindControl<CodeEditor>("MessageEditor")!.Bounds);
            window.Width = 390; Save(window, "android-keyboard-layout.png");
            Assert.True(socket.FindControl<CodeEditor>("MessageEditor")!.Bounds.Height >= 50, "Short portrait editor bounds: " + socket.FindControl<CodeEditor>("MessageEditor")!.Bounds);
        }
        finally { ThemeService.Apply("Dark"); await main.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task MobileDialogsCancelWithBackAndSettingsSaveWithoutDesktopWindows()
    {
        var view = new MobileMainView(); var window = new Window { Content = view, Width = 390, Height = 844 }; window.Show();
        var service = new MobileDialogService(() => view, Path.GetTempPath());
        try
        {
            var prompt = service.PromptAsync("在线 JSON 地址", "https://example.test/v3/api-docs");
            Save(window, "android-dialog.png"); Assert.True(view.HandleBack()); Assert.Null(await prompt);
            var workspace = new WorkspaceDocument();
            var editing = service.EditSettingsAsync(workspace);
            var settings = view.GetVisualDescendants().OfType<MobileSettingsView>().Single();
            var model = (ApiNet.Desktop.ViewModels.Settings.SettingsViewModel)settings.DataContext!;
            model.Theme = "浅色"; model.SaveCommand.Execute(null); await editing;
            Assert.Equal("Light", workspace.Settings.Theme); Assert.False(view.DialogHost.IsVisible);
            var environments = service.EditEnvironmentsAsync(workspace); Save(window, "android-variables.png"); Assert.True(view.HandleBack()); await environments;
        }
        finally { window.Close(); }
    }
    private static void Save(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Environment.GetEnvironmentVariable("APINET_SCREENSHOT_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(path); using var image = window.CaptureRenderedFrame(); Assert.NotNull(image); image.Save(Path.Combine(path, name));
    }
}
