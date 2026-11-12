using System.Text.Json;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Xml.Linq;
using ApiNet.Core.Models;
using ApiNet.Core.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels.Tabs;

public abstract partial class RequestTabViewModel : ViewModelBase, IAsyncDisposable
{
    protected readonly MainWindowViewModel Owner;
    public RequestDefinition Original
    {
        get; private set;
    }
    public string Id => Original.Id;
    public bool IsWebSocket => Original.Protocol == "WebSocket";
    public bool IsHttp => !IsWebSocket;
    public static string[] Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE"];
    public string[] MethodOptions => IsWebSocket ? ["WS"] : Methods;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Title))] private string _name;
    [ObservableProperty] private string _method;
    [ObservableProperty] private string _url;
    [ObservableProperty] private string _headers;
    [ObservableProperty] private string _body;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsFormBody), nameof(IsRawBody), nameof(IsMultipartBody))] private string _bodyFormat;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(FormatResponseCommand), nameof(MinifyResponseCommand))] private string _responseFormat = "Text";
    public static string[] BodyFormats { get; } = ["JSON", "XML", "Text", "HTML", "JavaScript", "Form URL Encoded", "Multipart"];
    public bool IsFormBody => BodyFormat is "Form URL Encoded" or "Multipart";
    public bool IsRawBody => !IsFormBody;
    public bool IsMultipartBody => BodyFormat == "Multipart";
    public ObservableCollection<ParameterRowViewModel> BodyRows { get; } = [];
    public ObservableCollection<ParameterRowViewModel> QueryRows { get; } = [];
    public ObservableCollection<ParameterRowViewModel> HeaderRows { get; } = [];
    public ParameterTableViewModel QueryTable { get; }
    public ParameterTableViewModel HeaderTable { get; }
    public ParameterTableViewModel BodyTable { get; }
    public string HeaderTabTitle => HeaderRows.Count(x => x.Key.Length > 0) is var count && count > 0 ? $"Headers ({count})" : "Headers";
    private bool _syncingRows;
    [ObservableProperty] private string _subprotocol;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _bearerToken = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Title))] private bool _isDirty;
    [ObservableProperty] private string _status = "准备就绪";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(FormatResponseCommand), nameof(MinifyResponseCommand), nameof(RestoreResponseCommand))] private string _responseBody = "";
    [ObservableProperty] private string _responseHeaders = "";
    [ObservableProperty] private string _resolvedUrl = "";
    [ObservableProperty] private bool _followRedirects;
    public string Title => Name + (IsDirty ? " ●" : "");
    public string Badge => IsWebSocket ? "WS" : Method;
    public string BadgeColor => ApiNet.Desktop.Services.ThemeService.MethodColor(Badge);
    public void RefreshTheme() => OnPropertyChanged(nameof(BadgeColor));
    public IAsyncRelayCommand SaveCommand
    {
        get;
    }
    public IAsyncRelayCommand CloseCommand
    {
        get;
    }
    public IAsyncRelayCommand DuplicateCommand
    {
        get;
    }
    public IAsyncRelayCommand CloseOtherTabsCommand { get; }
    public IAsyncRelayCommand CloseTabsToLeftCommand { get; }
    public IAsyncRelayCommand CloseTabsToRightCommand { get; }
    public IAsyncRelayCommand CloseAllTabsCommand { get; }

    private bool CanClose => !Owner.IsClosingTabs && Owner.Tabs.Contains(this);
    internal void RefreshCloseCommands()
    {
        CloseCommand.NotifyCanExecuteChanged();
        CloseOtherTabsCommand.NotifyCanExecuteChanged();
        CloseTabsToLeftCommand.NotifyCanExecuteChanged();
        CloseTabsToRightCommand.NotifyCanExecuteChanged();
        CloseAllTabsCommand.NotifyCanExecuteChanged();
    }

    protected RequestTabViewModel(RequestDefinition request, MainWindowViewModel owner)
    {
        Owner = owner;
        QueryTable = new ParameterTableViewModel(() => Owner.Dialogs.OpenUploadFileAsync()); QueryTable.SetRows(QueryRows);
        HeaderTable = new ParameterTableViewModel(() => Owner.Dialogs.OpenUploadFileAsync()); HeaderTable.SetRows(HeaderRows);
        BodyTable = new ParameterTableViewModel(() => Owner.Dialogs.OpenUploadFileAsync()); BodyTable.SetRows(BodyRows);
        foreach (var table in new[] { QueryTable, HeaderTable, BodyTable })
            table.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ParameterTableViewModel.HasPendingBulkChanges)) IsDirty = HasChanges(); };
        request = NormalizeQuery(request);
        Original = ModelCopy.Request(request);
        _name = request.Name;
        _method = request.Method;
        _url = request.Url;
        _headers = request.Headers;
        _body = request.Body;
        _bodyFormat = request.BodyFormat;
        _subprotocol = request.Subprotocol;
        _followRedirects = request.FollowRedirects;
        QueryRows.CollectionChanged += RowsChanged;
        HeaderRows.CollectionChanged += RowsChanged;
        BodyRows.CollectionChanged += RowsChanged;
        LoadRows(request);
        SaveCommand = new AsyncRelayCommand(() => Owner.SaveTabAsync(this));
        CloseCommand = new AsyncRelayCommand(() => Owner.CloseTabAsync(this), () => CanClose);
        CloseOtherTabsCommand = new AsyncRelayCommand(() => Owner.CloseOtherTabsAsync(this), () => CanClose && Owner.Tabs.Count > 1);
        CloseTabsToLeftCommand = new AsyncRelayCommand(() => Owner.CloseTabsToLeftAsync(this), () => CanClose && Owner.Tabs.IndexOf(this) > 0);
        CloseTabsToRightCommand = new AsyncRelayCommand(() => Owner.CloseTabsToRightAsync(this), () => CanClose && Owner.Tabs.IndexOf(this) < Owner.Tabs.Count - 1);
        CloseAllTabsCommand = new AsyncRelayCommand(() => Owner.CloseAllTabsAsync(), () => CanClose);
        DuplicateCommand = new AsyncRelayCommand(() => Owner.DuplicateTabAsync(this));
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Name) or nameof(Method) or nameof(Url) or nameof(Headers) or nameof(Body) or nameof(BodyFormat) or nameof(Subprotocol) or nameof(Query) or nameof(BearerToken) or nameof(FollowRedirects))
                IsDirty = HasChanges();
            if (args.PropertyName == nameof(Method))
            {
                OnPropertyChanged(nameof(Badge));
                OnPropertyChanged(nameof(BadgeColor));
            }
        };
    }

    public RequestDefinition Snapshot()
    {
        // Background draft persistence also includes valid edits still open in Bulk Edit.
        // Invalid input stays in the editor; explicit save/send validates it separately.
        foreach (var table in new[] { QueryTable, HeaderTable, BodyTable }) table.TryApplyBulk();
        var request = ModelCopy.Request(Original);
        request.Name = Name;
        request.Method = IsWebSocket ? "WS" : Method;
        request.Url = QueryUrl.WithoutQuery(Url);
        request.Headers = Headers;
        request.Body = Body;
        request.BodyFormat = BodyFormat;
        request.QueryParameters = QueryRows.Select(x => x.Snapshot()).ToList();
        request.HeaderParameters = HeaderRows.Select(x => x.Snapshot()).ToList();
        request.BodyParameters = BodyRows.Select(x => x.Snapshot()).ToList();
        request.Subprotocol = Subprotocol;
        request.FollowRedirects = FollowRedirects;
        if (!string.IsNullOrWhiteSpace(Query))
        {
            var extra = Query.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !x.StartsWith('#')).Select(x => { var i = x.IndexOf('='); return i < 0 ? Uri.EscapeDataString(x) : Uri.EscapeDataString(x[..i].Trim()) + "=" + x[(i + 1)..].Trim(); });
            request.Url += (request.Url.Contains('?') ? "&" : "?") + string.Join('&', extra);
        }
        if (!string.IsNullOrWhiteSpace(BearerToken))
        {
            request.HeaderParameters.RemoveAll(x => x.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
            request.HeaderParameters.Add(new RequestParameter { Key = "Authorization", Value = "Bearer " + BearerToken });
            request.Headers = string.Join('\n', request.HeaderParameters.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)).Select(x => x.Key.Trim() + ": " + x.Value));
        }
        return request;
    }

    private bool HasChanges() => QueryTable.HasPendingBulkChanges || HeaderTable.HasPendingBulkChanges || BodyTable.HasPendingBulkChanges || Owner.Workspace.Requests.All(x => x.Id != Id) || Name != Original.Name || Method != Original.Method || QueryUrl.WithoutQuery(Url) != Original.Url || Headers != Original.Headers || Body != Original.Body || BodyFormat != Original.BodyFormat || Subprotocol != Original.Subprotocol || FollowRedirects != Original.FollowRedirects || !string.IsNullOrEmpty(Query) || !string.IsNullOrEmpty(BearerToken) || !SameRows(BodyRows, Original.BodyParameters) || !SameRows(QueryRows, Original.QueryParameters) || !SameRows(HeaderRows, HeaderValues(Original));

    private static bool SameRows(IEnumerable<ParameterRowViewModel> rows, IEnumerable<RequestParameter> values) => rows.Select(x => (x.Enabled, x.Key, x.Value, x.Type, x.Description)).SequenceEqual(values.Select(x => (x.Enabled, x.Key, x.Value, x.Type, x.Description)));
    private static IEnumerable<RequestParameter> HeaderValues(RequestDefinition request) => request.HeaderParameters.Count > 0 ? request.HeaderParameters : request.Headers.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { var index = line.IndexOf(':'); return new RequestParameter { Key = index < 0 ? line.Trim() : line[..index].Trim(), Value = index < 0 ? "" : line[(index + 1)..].Trim() }; });
    private void LoadRows(RequestDefinition request)
    {
        _syncingRows = true;
        QueryRows.Clear(); HeaderRows.Clear(); BodyRows.Clear();
        foreach (var parameter in request.QueryParameters) AddRow(QueryRows, parameter);
        foreach (var parameter in HeaderValues(request)) AddRow(HeaderRows, parameter);
        foreach (var parameter in request.BodyParameters) AddRow(BodyRows, parameter);
        Url = QueryUrl.WithQuery(request.Url, request.QueryParameters);
        _syncingRows = false;
    }
    private void AddRow(ObservableCollection<ParameterRowViewModel> rows, RequestParameter parameter) => rows.Add(new ParameterRowViewModel(parameter, row => rows.Remove(row), () => Owner.Dialogs.OpenUploadFileAsync()));
    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
            foreach (ParameterRowViewModel row in args.NewItems) row.PropertyChanged += RowChanged;
        if (args.OldItems is not null)
            foreach (ParameterRowViewModel row in args.OldItems) row.PropertyChanged -= RowChanged;
        UpdateRows(ReferenceEquals(sender, QueryRows));
    }
    private void RowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => UpdateRows(sender is ParameterRowViewModel row && QueryRows.Contains(row));
    private void UpdateRows(bool queryChanged)
    {
        OnPropertyChanged(nameof(HeaderTabTitle));
        if (_syncingRows) return;
        _syncingRows = true;
        Headers = string.Join('\n', HeaderRows.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)).Select(x => x.Key.Trim() + ": " + x.Value));
        if (queryChanged) Url = QueryUrl.WithQuery(Url, QueryRows.Select(x => x.Snapshot()));
        _syncingRows = false;
        IsDirty = HasChanges();
    }
    partial void OnHeadersChanged(string value)
    {
        if (_syncingRows) return;
        _syncingRows = true;
        HeaderRows.Clear();
        foreach (var parameter in HeaderValues(new RequestDefinition { Headers = value })) AddRow(HeaderRows, parameter);
        _syncingRows = false;
    }
    [RelayCommand] private void AddQuery() => AddRow(QueryRows, new RequestParameter());
    [RelayCommand] private void AddHeader() => AddRow(HeaderRows, new RequestParameter());
    [RelayCommand] private void AddBodyParameter() => AddRow(BodyRows, new RequestParameter());

    public void MarkSaved(RequestDefinition request)
    {
        request = NormalizeQuery(request);
        Original = ModelCopy.Request(request);
        Query = "";
        BearerToken = "";
        Url = request.Url;
        Headers = request.Headers;
        LoadRows(request);
        IsDirty = false;
    }
    public void ApplyImportedDefinition(RequestDefinition request)
    {
        request = NormalizeQuery(request);
        var baseUnchanged = QueryUrl.WithoutQuery(Url) == Original.Url;
        var queryUnchanged = SameRows(QueryRows, Original.QueryParameters);
        if (Name == Original.Name)
            Name = request.Name;
        if (Method == Original.Method)
            Method = request.Method;
        if (Headers == Original.Headers && SameRows(HeaderRows, HeaderValues(Original)))
        {
            Headers = request.Headers;
            _syncingRows = true;
            HeaderRows.Clear();
            foreach (var parameter in HeaderValues(request)) AddRow(HeaderRows, parameter);
            _syncingRows = false;
        }
        if (Body == Original.Body)
            Body = request.Body;
        if (BodyFormat == Original.BodyFormat)
            BodyFormat = request.BodyFormat;
        if (SameRows(BodyRows, Original.BodyParameters))
        {
            _syncingRows = true;
            BodyRows.Clear();
            foreach (var parameter in request.BodyParameters) AddRow(BodyRows, parameter);
            _syncingRows = false;
        }
        if (queryUnchanged)
        {
            _syncingRows = true;
            QueryRows.Clear();
            foreach (var parameter in request.QueryParameters) AddRow(QueryRows, parameter);
            _syncingRows = false;
        }
        _syncingRows = true;
        Url = QueryUrl.WithQuery(baseUnchanged ? request.Url : Url, QueryRows.Select(x => x.Snapshot()));
        _syncingRows = false;
        if (Subprotocol == Original.Subprotocol)
            Subprotocol = request.Subprotocol;
        Original = ModelCopy.Request(request);
        IsDirty = HasChanges();
    }
    public RequestDefinition Resolve(WorkspaceDocument? context = null)
    {
        if (!TryCommitParameterEdits()) throw new InvalidOperationException(Status);
        var request = Owner.Variables.ResolveRequest(Snapshot(), context ?? Owner.Workspace);
        ResolvedUrl = request.Url;
        return request;
    }

    [RelayCommand]
    private void Preview()
    {
        try
        {
            ResolvedUrl = Resolve().Url;
            Status = "变量解析完成";
        }
        catch (Exception exception) { Status = exception.Message; }
    }

    [RelayCommand]
    private void FormatBody()
    {
        try
        {
            if (BodyFormat is "XML" or "HTML")
            {
                Body = XDocument.Parse(Body).ToString();
                return;
            }
            using var json = JsonDocument.Parse(Body);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                json.WriteTo(writer);
            Body = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception exception) when (exception is JsonException or System.Xml.XmlException) { Status = "格式错误：" + exception.Message; }
    }
    public virtual Task StopAsync() => Task.CompletedTask;
    public bool TryCommitParameterEdits()
    {
        foreach (var table in new[] { QueryTable, HeaderTable, BodyTable })
            if (!table.TryApplyBulk()) { Status = "参数批量编辑无效：" + table.BulkError; return false; }
        return true;
    }
    public void DisposeParameterTables() { QueryTable.Dispose(); HeaderTable.Dispose(); BodyTable.Dispose(); }
    public abstract ValueTask DisposeAsync();
}
