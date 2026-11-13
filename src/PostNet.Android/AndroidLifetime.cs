using PostNet.Desktop.Services;
using PostNet.Desktop.ViewModels;
using PostNet.Desktop.ViewModels.Mobile;
using PostNet.Desktop.Views.Mobile;
using PostNet.Infrastructure.Http;
using PostNet.Infrastructure.Imports;
using PostNet.Infrastructure.OpenApi;
using PostNet.Infrastructure.Persistence;
using PostNet.Infrastructure.Variables;
using PostNet.Proxy;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace PostNet.Android;

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
        _main.ExitRequested += _ => _updates.Apply(true);
        _view.AttachedToVisualTree += InitializeOnce;
        lifetime.MainView = _view;
    }
    private async void InitializeOnce(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _view!.AttachedToVisualTree -= InitializeOnce;
        try { await _main!.InitializeAsync(); global::Android.Util.Log.Info("Post.NET", "Workspace initialized: " + _main.Workspace.Requests.Count + " requests"); }
        catch (Exception exception) { _main!.Status = "加载失败：" + exception.Message; global::Android.Util.Log.Error("Post.NET", exception.ToString()); }
    }
    public void SaveDrafts() { if (_main is not null) Dispatcher.UIThread.Post(() => _ = _main.SaveDraftsAsync()); }
    public void ResumeInstall() => _updates?.ResumeInstall();
    public bool HandleBack() => _view?.HandleBack() == true;
}
