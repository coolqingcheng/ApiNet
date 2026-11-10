using System.Collections.ObjectModel;
using Avalonia.Threading;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels.Tabs;

public partial class WebSocketTabViewModel : RequestTabViewModel
{
    private readonly IWebSocketSession _session;
    private HistoryEntry? _history;
    private WorkspaceDocument? _executionContext;
    private CancellationTokenSource? _connecting;
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(MessageLanguage))] private bool _binary;
    public static string[] MessageFormats { get; } = ["JSON", "XML", "Text", "HTML", "JavaScript"];
    public string MessageLanguage => Binary ? "Text" : BodyFormat;
    public ObservableCollection<SocketMessage> Messages { get; } = [];
    public WebSocketTabViewModel(RequestDefinition request, MainWindowViewModel owner, IWebSocketSession session) : base(request, owner)
    {
        _session = session;
        PropertyChanged += (_, args) => { if (args.PropertyName == nameof(BodyFormat)) OnPropertyChanged(nameof(MessageLanguage)); };
        _session.MessageReceived += message => Dispatcher.UIThread.Post(async () =>
        {
            Messages.Add(message);
            if (Messages.Count > 1000)
                Messages.RemoveAt(0);
            IsConnected = _session.IsConnected;
            if (message.Kind is "状态" or "关闭" or "错误")
                Status = message.Content;
            if (_history is not null)
            {
                if (_history.Messages.Count < 1000)
                    _history.Messages.Add(new SocketMessage { Timestamp = message.Timestamp, Direction = message.Direction, Kind = message.Kind, Content = _executionContext is null ? message.Content : HistorySanitizer.Redact(message.Content, _executionContext) });
                if (!IsConnected)
                    await PersistSessionAsync();
            }
        });
    }
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy || IsConnected)
            return;
        IsBusy = true;
        try
        {
            _executionContext = HistorySanitizer.SnapshotContext(Owner.Workspace);
            var request = Resolve(_executionContext);
            Messages.Clear();
            _history = HistorySanitizer.Create(request, new HttpResult { StatusCode = 101, StatusText = "WebSocket 会话" }, _executionContext);
            using var connecting = new CancellationTokenSource();
            _connecting = connecting;
            await _session.ConnectAsync(request, connecting.Token);
            IsConnected = _session.IsConnected;
            Status = IsConnected ? "已连接" : "连接已结束";
            await PersistSessionAsync();
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            if (_history is not null)
            {
                _history.Response.Error = exception.Message;
                await PersistSessionAsync();
            }
        }
        finally { _connecting = null; IsBusy = false; }
    }
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _connecting?.Cancel();
        if (ConnectCommand.ExecutionTask is { IsCompleted: false } running)
            await running;
        await _session.DisconnectAsync();
        IsConnected = false;
        Status = "已断开";
        await PersistSessionAsync();
    }
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        try
        {
            await _session.SendAsync(Owner.Variables.Resolve(Body, _executionContext ?? Owner.Workspace), Binary, CancellationToken.None);
        }
        catch (Exception exception) { Status = exception.Message; }
    }
    private async Task PersistSessionAsync()
    {
        await _historyGate.WaitAsync();
        try
        {
            if (_history is not null)
                await Owner.RecordHistoryAsync(_history);
        }
        catch (Exception exception) { Status = "保存会话失败：" + exception.Message; }
        finally { _historyGate.Release(); }
    }
    public override Task StopAsync() => DisconnectAsync();
    public override async ValueTask DisposeAsync()
    {
        _connecting?.Cancel();
        if (ConnectCommand.ExecutionTask is { IsCompleted: false } running)
            await running;
        await _session.DisposeAsync();
        IsConnected = false;
        await PersistSessionAsync();
    }
}
