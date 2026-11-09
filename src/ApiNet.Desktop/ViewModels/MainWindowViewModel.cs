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

public partial class MainWindowViewModel : ViewModelBase
{
    public string ApplicationVersion => "ApiNet " + typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3);
    private readonly IWorkspaceStore _store;
    private readonly IHttpRequestService _http;
    private readonly IOpenApiService _openApi;
    private readonly IProxyService _proxy;
    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;
    private readonly List<HistoryEntry> _allHistory = [];
    private readonly List<CaptureRecord> _allCaptures = [];
    private bool _initializing;
    public WorkspaceDocument Workspace { get; private set; } = new();
    public IVariableResolver Variables
    {
        get;
    }
    public IDialogService Dialogs => _dialogs;
    public ObservableCollection<WorkspaceNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<RequestTabViewModel> Tabs { get; } = [];
    public ObservableCollection<EnvironmentDefinition> Environments { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public ObservableCollection<CaptureRecord> Captures { get; } = [];
    public ObservableCollection<string> HistoryEnvironments { get; } = ["全部环境"];
    public static string[] HistoryMethods { get; } = ["全部方法", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "WS"];
    public static string[] HistoryResults { get; } = ["全部结果", "成功", "HTTP 错误", "网络错误"];
    public static string[] HistoryPeriods { get; } = ["全部时间", "今天", "最近 7 天", "最近 30 天"];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsWorkspace), nameof(IsHistory), nameof(IsCapture))] private string _activePanel = "工作区";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private WorkspaceNodeViewModel? _selectedNode;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasTabs), nameof(IsEmpty))] private RequestTabViewModel? _selectedTab;
    [ObservableProperty] private EnvironmentDefinition? _selectedEnvironment;
    [ObservableProperty] private string _status = "准备就绪";
    [ObservableProperty] private string _historySearch = "";
    [ObservableProperty] private string _historyMethod = "全部方法";
    [ObservableProperty] private string _historyResult = "全部结果";
    [ObservableProperty] private string _historyPeriod = "全部时间";
    [ObservableProperty] private string _historyEnvironment = "全部环境";
    [ObservableProperty] private DateTimeOffset? _historyFrom;
    [ObservableProperty] private DateTimeOffset? _historyTo;
    [ObservableProperty] private HistoryEntry? _selectedHistory;
    [ObservableProperty] private string _captureSearch = "";
    [ObservableProperty] private CaptureRecord? _selectedCapture;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ProxyText))] private bool _proxyRunning;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasUpdateStatus))] private string _updateStatus = "";
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private bool _updateReady;
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private bool _updateOnExit;
    public bool IsWorkspace => ActivePanel == "工作区";
    public bool IsHistory => ActivePanel == "历史记录";
    public bool IsCapture => ActivePanel == "代理抓包";
    public bool HasTabs => Tabs.Count > 0;
    public bool IsEmpty => Tabs.Count == 0;
    public bool HasUpdateStatus => UpdateStatus.Length > 0;
    public string ProxyText => ProxyRunning ? $"● 代理 127.0.0.1:{Workspace.Settings.ProxyPort}" : "○ 代理未启动";
    public event Action<bool>? ExitRequested;

    public MainWindowViewModel(IWorkspaceStore store, IVariableResolver variables, IHttpRequestService http, IOpenApiService openApi, IProxyService proxy, IUpdateService updates, IDialogService dialogs)
    {
        _store = store;
        Variables = variables;
        _http = http;
        _openApi = openApi;
        _proxy = proxy;
        _updates = updates;
        _dialogs = dialogs;
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Search))
                RefreshTree();
            if (args.PropertyName is nameof(HistorySearch) or nameof(HistoryMethod) or nameof(HistoryResult) or nameof(HistoryPeriod) or nameof(HistoryEnvironment) or nameof(HistoryFrom) or nameof(HistoryTo))
                FilterHistory();
            if (args.PropertyName == nameof(CaptureSearch))
                FilterCaptures();
        };
        Tabs.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasTabs)); OnPropertyChanged(nameof(IsEmpty)); };
        _proxy.Captured += record => Dispatcher.UIThread.Post(() => { _allCaptures.Insert(0, record); if (_allCaptures.Count > 1000) _allCaptures.RemoveAt(_allCaptures.Count - 1); FilterCaptures(); });
        _updates.StateChanged += state => Dispatcher.UIThread.Post(() => { UpdateStatus = state.Version is null ? state.Status : $"v{state.Version} · {state.Status}"; UpdateProgress = state.Progress; UpdateReady = state.Ready; UpdateBusy = state.Busy; });
    }

    public async Task InitializeAsync()
    {
        _initializing = true;
        try
        {
            Workspace = await _store.LoadAsync() ?? CreateWorkspace();
            RefreshEnvironments();
            RefreshTree();
            _allHistory.AddRange(await _store.GetHistoryAsync());
            FilterHistory();
            foreach (var draft in Workspace.OpenTabs.Take(30))
            {
                var tab = CreateTab(draft);
                tab.IsDirty = Workspace.Requests.All(x => x.Id != draft.Id) || !SameContent(Workspace.Requests.FirstOrDefault(x => x.Id == draft.Id), draft);
                Tabs.Add(tab);
            }
            if (Tabs.Count == 0 && Workspace.Requests.FirstOrDefault() is { } initial)
                OpenRequest(initial);
            SelectedTab = Tabs.FirstOrDefault();
            Status = $"本地工作区 · {Workspace.Requests.Count} 个请求";
            await PersistAsync();
        }
        catch (Exception exception) { Status = "加载工作区失败：" + exception.Message; throw; }
        finally { _initializing = false; }
        if (Workspace.Settings.AutoCheckUpdates && Workspace.Settings.UpdateUrl.Length > 0)
            _ = _updates.CheckAndDownloadAsync(Workspace.Settings.UpdateUrl);
    }

    private static WorkspaceDocument CreateWorkspace()
    {
        var folder = new FolderDefinition { Name = "快速开始" };
        var environment = new EnvironmentDefinition { Name = "开发环境", Variables = [new() { Key = "baseUrl", Value = "https://httpbin.org" }, new() { Key = "wsBaseUrl", Value = "wss://ws.postman-echo.com" }] };
        return new WorkspaceDocument
        {
            Folders = [folder],
            Environments = [environment],
            SelectedEnvironmentId = environment.Id,
            Requests = [new() { FolderId = folder.Id, Name = "获取请求信息", Url = "{{baseUrl}}/get" }, new() { FolderId = folder.Id, Name = "发送 JSON", Method = "POST", Url = "{{baseUrl}}/post", Headers = "Content-Type: application/json", Body = "{\n  \"message\": \"Hello, ApiNet\"\n}" }, new() { FolderId = folder.Id, Name = "WebSocket Echo", Protocol = "WebSocket", Method = "WS", Url = "{{wsBaseUrl}}/raw", Body = "Hello, ApiNet" }]
        };
    }

    private void RefreshEnvironments()
    {
        Environments.Clear();
        Environments.Add(new EnvironmentDefinition { Id = "", Name = "仅公共变量" });
        foreach (var environment in Workspace.Environments)
            Environments.Add(environment);
        SelectedEnvironment = Environments.FirstOrDefault(x => x.Id == Workspace.SelectedEnvironmentId) ?? Environments[0];
    }
    partial void OnSelectedEnvironmentChanged(EnvironmentDefinition? value)
    {
        Workspace.SelectedEnvironmentId = string.IsNullOrEmpty(value?.Id) ? null : value.Id;
        if (!_initializing)
            _ = PersistSafelyAsync();
    }
    [RelayCommand]
    private async Task ManageEnvironmentsAsync()
    {
        await _dialogs.EditEnvironmentsAsync(Workspace);
        _initializing = true;
        RefreshEnvironments();
        _initializing = false;
        await PersistSafelyAsync();
    }
    [RelayCommand]
    private async Task SettingsAsync()
    {
        await _dialogs.EditSettingsAsync(Workspace);
        OnPropertyChanged(nameof(ProxyText));
        await PersistSafelyAsync();
    }
    [RelayCommand] private Task CheckUpdatesAsync() => _updates.CheckAndDownloadAsync(Workspace.Settings.UpdateUrl);
    [RelayCommand]
    private void UpgradeNow()
    {
        if (_updates.IsReady)
            ExitRequested?.Invoke(true);
    }
    [RelayCommand]
    private void UpgradeOnExit()
    {
        UpdateOnExit = true;
        UpdateStatus = "将在真正退出应用时安装更新；隐藏到托盘会继续运行。";
    }
    [RelayCommand] private void DismissUpdate() => UpdateStatus = "";

    private static bool SameContent(RequestDefinition? left, RequestDefinition right) => left is not null && left.Name == right.Name && left.Url == right.Url && left.Method == right.Method && left.Body == right.Body && left.BodyFormat == right.BodyFormat && left.Headers == right.Headers && left.Subprotocol == right.Subprotocol && left.FollowRedirects == right.FollowRedirects && SameParameters(left.QueryParameters, right.QueryParameters) && SameParameters(left.HeaderParameters, right.HeaderParameters);
    private static bool SameParameters(IEnumerable<RequestParameter> left, IEnumerable<RequestParameter> right) => left.Select(x => (x.Enabled, x.Key, x.Value)).SequenceEqual(right.Select(x => (x.Enabled, x.Key, x.Value)));
    private Task PersistAsync()
    {
        Workspace.OpenTabs = Tabs.Select(x => x.Snapshot()).ToList();
        return _store.SaveAsync(Workspace);
    }
    private async Task PersistSafelyAsync()
    {
        try
        {
            await PersistAsync();
        }
        catch (Exception exception) { Status = "保存失败：" + exception.Message; }
    }
    public async Task ShutdownAsync()
    {
        foreach (var tab in Tabs.ToArray())
            await tab.StopAsync();
        await _proxy.StopAsync();
        await PersistAsync();
    }
    public void ApplyUpdate(bool restart)
    {
        if (_updates.IsReady && (restart || UpdateOnExit))
            _updates.Apply(restart);
    }
}
