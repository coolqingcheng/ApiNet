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
    [ObservableProperty] private string _bodyFormat;
    [ObservableProperty] private string _responseFormat = "Text";
    public static string[] BodyFormats { get; } = ["JSON", "XML", "Text", "HTML", "JavaScript"];
    public ObservableCollection<ParameterRowViewModel> QueryRows { get; } = [];
    public ObservableCollection<ParameterRowViewModel> HeaderRows { get; } = [];
    private bool _syncingRows;
    [ObservableProperty] private string _subprotocol;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _bearerToken = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Title))] private bool _isDirty;
    [ObservableProperty] private string _status = "准备就绪";
    [ObservableProperty] private string _responseBody = "";
    [ObservableProperty] private string _responseHeaders = "";
    [ObservableProperty] private string _resolvedUrl = "";
    [ObservableProperty] private bool _followRedirects;
    public string Title => Name + (IsDirty ? " ●" : "");
    public string Badge => IsWebSocket ? "WS" : Method;
    public string BadgeColor => IsWebSocket ? "#BBA3FF" : Method switch { "GET" => "#7CD9B2", "POST" => "#F1CA76", "DELETE" => "#F18B96", _ => "#8CB8F8" };
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

    protected RequestTabViewModel(RequestDefinition request, MainWindowViewModel owner)
    {
        Owner = owner;
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
        LoadRows(request);
        SaveCommand = new AsyncRelayCommand(() => Owner.SaveTabAsync(this));
        CloseCommand = new AsyncRelayCommand(() => Owner.CloseTabAsync(this));
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
        var request = ModelCopy.Request(Original);
        request.Name = Name;
        request.Method = IsWebSocket ? "WS" : Method;
        request.Url = Url;
        request.Headers = Headers;
        request.Body = Body;
        request.BodyFormat = BodyFormat;
        request.QueryParameters = QueryRows.Select(x => x.Snapshot()).ToList();
        request.HeaderParameters = HeaderRows.Select(x => x.Snapshot()).ToList();
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

    private bool HasChanges() => Owner.Workspace.Requests.All(x => x.Id != Id) || Name != Original.Name || Method != Original.Method || Url != Original.Url || Headers != Original.Headers || Body != Original.Body || BodyFormat != Original.BodyFormat || Subprotocol != Original.Subprotocol || FollowRedirects != Original.FollowRedirects || !string.IsNullOrEmpty(Query) || !string.IsNullOrEmpty(BearerToken) || !SameRows(QueryRows, Original.QueryParameters) || !SameRows(HeaderRows, HeaderValues(Original));

    private static bool SameRows(IEnumerable<ParameterRowViewModel> rows, IEnumerable<RequestParameter> values) => rows.Select(x => (x.Enabled, x.Key, x.Value)).SequenceEqual(values.Select(x => (x.Enabled, x.Key, x.Value)));
    private static IEnumerable<RequestParameter> HeaderValues(RequestDefinition request) => request.HeaderParameters.Count > 0 ? request.HeaderParameters : request.Headers.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { var index = line.IndexOf(':'); return new RequestParameter { Key = index < 0 ? line.Trim() : line[..index].Trim(), Value = index < 0 ? "" : line[(index + 1)..].Trim() }; });
    private void LoadRows(RequestDefinition request)
    {
        _syncingRows = true;
        QueryRows.Clear(); HeaderRows.Clear();
        foreach (var parameter in request.QueryParameters) AddRow(QueryRows, parameter);
        foreach (var parameter in HeaderValues(request)) AddRow(HeaderRows, parameter);
        _syncingRows = false;
    }
    private void AddRow(ObservableCollection<ParameterRowViewModel> rows, RequestParameter parameter) => rows.Add(new ParameterRowViewModel(parameter, row => rows.Remove(row)));
    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
            foreach (ParameterRowViewModel row in args.NewItems) row.PropertyChanged += RowChanged;
        if (args.OldItems is not null)
            foreach (ParameterRowViewModel row in args.OldItems) row.PropertyChanged -= RowChanged;
        UpdateRows();
    }
    private void RowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => UpdateRows();
    private void UpdateRows()
    {
        if (_syncingRows) return;
        _syncingRows = true;
        Headers = string.Join('\n', HeaderRows.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)).Select(x => x.Key.Trim() + ": " + x.Value));
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

    public void MarkSaved(RequestDefinition request)
    {
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
        if (Name == Original.Name)
            Name = request.Name;
        if (Method == Original.Method)
            Method = request.Method;
        if (Url == Original.Url)
            Url = request.Url;
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
        if (SameRows(QueryRows, Original.QueryParameters))
        {
            _syncingRows = true;
            QueryRows.Clear();
            foreach (var parameter in request.QueryParameters) AddRow(QueryRows, parameter);
            _syncingRows = false;
        }
        if (Subprotocol == Original.Subprotocol)
            Subprotocol = request.Subprotocol;
        Original = ModelCopy.Request(request);
        IsDirty = HasChanges();
    }
    public void ShowHistory(HttpResult response)
    {
        ResponseBody = response.Body;
        ResponseHeaders = response.Headers;
        ResponseFormat = response.ContentType.Contains("json") ? "JSON" : response.ContentType.Contains("xml") ? "XML" : response.ContentType.Contains("html") ? "HTML" : "Text";
        Status = response.Error ?? $"{response.StatusCode} {response.StatusText}  ·  {response.DurationMs} ms  ·  {response.Size:N0} B";
    }
    public RequestDefinition Resolve(WorkspaceDocument? context = null)
    {
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
    public abstract ValueTask DisposeAsync();
}
