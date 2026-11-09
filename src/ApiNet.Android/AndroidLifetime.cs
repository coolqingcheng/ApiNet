using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Mobile;
using ApiNet.Desktop.Views.Mobile;
using ApiNet.Infrastructure.Http;
using ApiNet.Infrastructure.Imports;
using ApiNet.Infrastructure.OpenApi;
using ApiNet.Infrastructure.Persistence;
using ApiNet.Infrastructure.Variables;
using ApiNet.Proxy;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace ApiNet.Android;

public sealed class AndroidLifetime(MainActivity activity)
{
    private MainWindowViewModel? _main;
    private MobileMainView? _view;
    private AndroidUpdateService? _updates;
    public void Initialize(Avalonia.Application app)
    {
        if (app.ApplicationLifetime is not ISingleViewApplicationLifetime lifetime) return;
        var directory = Path.Combine(activity.FilesDir!.AbsolutePath, "workspace");
        var store = new SqliteWorkspaceStore(directory);
        _view = new MobileMainView();
        var dialogs = new MobileDialogService(() => _view, directory);
        _updates = new AndroidUpdateService(activity);
        var openApi = new OpenApiService();
        _main = new MainWindowViewModel(store, new VariableResolver(), new HttpRequestService(), openApi, new ForwardProxyService(directory), _updates, dialogs, new AndroidImportService(new WorkspaceImportService(openApi))) { IsMobile = true };
        _view.DataContext = new MobileShellViewModel(_main);
        lifetime.MainView = _view;
        _main.ExitRequested += _ => _updates.Apply(true);
        _view.AttachedToVisualTree += InitializeOnce;
    }
    private async void InitializeOnce(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _view!.AttachedToVisualTree -= InitializeOnce;
        try { await _main!.InitializeAsync(); }
        catch (Exception exception) { _main!.Status = "加载失败：" + exception.Message; }
    }
    public void SaveDrafts() { if (_main is not null) Dispatcher.UIThread.Post(() => _ = _main.SaveDraftsAsync()); }
    public void ResumeInstall() => _updates?.ResumeInstall();
    public bool HandleBack() => _view?.HandleBack() == true;
}
