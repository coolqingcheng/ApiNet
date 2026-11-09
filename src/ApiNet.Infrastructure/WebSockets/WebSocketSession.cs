using System.Net.WebSockets;
using System.Text;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Http;

namespace ApiNet.Infrastructure.WebSockets;

public sealed class WebSocketSession : IWebSocketSession
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _receiver;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    public event Action<SocketMessage>? MessageReceived;
    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(RequestDefinition request, CancellationToken cancellationToken)
    {
        await DisconnectAsync();
        var uri = new Uri(request.Url);
        if (uri.Scheme is not ("ws" or "wss"))
            throw new FormatException("WebSocket 地址需要以 ws:// 或 wss:// 开头。");
        _socket = new ClientWebSocket();
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        foreach (var header in HeaderParser.Parse(request.Headers))
            _socket.Options.SetRequestHeader(header.Key, header.Value);
        foreach (var protocol in request.Subprotocol.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            _socket.Options.AddSubProtocol(protocol);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 3600)));
        await _socket.ConnectAsync(uri, timeout.Token);
        _lifetime = new CancellationTokenSource();
        Emit("系统", "连接", $"已连接 {uri}");
        _receiver = ReceiveAsync(_socket, _lifetime.Token);
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Emit("系统", "关闭", $"{result.CloseStatus}: {result.CloseStatusDescription}");
                        if (socket.State == WebSocketState.CloseReceived)
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        return;
                    }
                    if (message.Length + result.Count > 4 * 1024 * 1024)
                        throw new InvalidOperationException("单条消息超过 4 MB，连接已断开。");
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                Emit("接收", result.MessageType.ToString(), result.MessageType == WebSocketMessageType.Binary ? Convert.ToBase64String(message.ToArray()) : Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Emit("系统", "错误", exception.Message); socket.Abort(); }
        finally { Emit("系统", "状态", "连接已结束"); }
    }

    public async Task SendAsync(string content, bool binary, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new InvalidOperationException("请先连接 WebSocket。");
        var data = binary ? Convert.FromBase64String(content) : Encoding.UTF8.GetBytes(content);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket!.SendAsync(new ArraySegment<byte>(data), binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, cancellationToken);
            Emit("发送", binary ? "Binary" : "Text", content);
        }
        finally { _sendLock.Release(); }
    }

    private void Emit(string direction, string kind, string content) => MessageReceived?.Invoke(new SocketMessage { Direction = direction, Kind = kind, Content = content });

    public async Task DisconnectAsync()
    {
        _lifetime?.Cancel();
        if (_socket?.State == WebSocketState.Open)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "用户断开", timeout.Token);
            }
            catch (Exception) { _socket.Abort(); }
        }
        if (_receiver is not null)
            await _receiver;
        _socket?.Dispose();
        _socket = null;
        _receiver = null;
        _lifetime?.Dispose();
        _lifetime = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
    }
}
