using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Proxy.Certificates;
using ApiNet.Proxy.Protocol;

namespace ApiNet.Proxy;

public sealed class ForwardProxyService : IProxyService
{
    private readonly CertificateAuthority _certificates;
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<TcpClient, Task> _connections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;
    public bool IsRunning => _listener is not null;
    public event Action<CaptureRecord>? Captured;
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase) { "Connection", "Proxy-Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate", "Expect" };

    public ForwardProxyService(string dataDirectory, HttpMessageHandler? upstreamHandler = null)
    {
        _certificates = new CertificateAuthority(dataDirectory);
        _client = new HttpClient(upstreamHandler ?? new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public string ExportCertificate() => _certificates.ExportPublicCertificate();

    public Task StartAsync(int port, bool decryptHttps)
    {
        if (IsRunning)
            return Task.CompletedTask;
        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "代理端口应在 1024–65535 之间。");
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        _listener = listener;
        _lifetime = new CancellationTokenSource();
        _acceptLoop = AcceptAsync(listener, decryptHttps, _lifetime.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptAsync(TcpListener listener, bool decryptHttps, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _connections[client] = completion.Task;
                _ = HandleTrackedAsync(client, decryptHttps, cancellationToken, completion);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task HandleTrackedAsync(TcpClient client, bool decryptHttps, CancellationToken cancellationToken, TaskCompletionSource completion)
    {
        try
        {
            using (client)
            {
                await using var stream = client.GetStream();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                var head = await HttpWire.ReadHeadAsync(stream, timeout.Token);
                if (head is null)
                    return;
                var parts = head.StartLine.Split(' ');
                if (parts.Length != 3)
                    throw new IOException("无效 HTTP 请求行。");
                if (parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    var destination = new Uri("https://" + parts[1]);
                    await HttpWire.WriteAsync(stream, "HTTP/1.1 200 Connection Established\r\n\r\n", timeout.Token);
                    var peek = new byte[1];
                    if (await client.Client.ReceiveAsync(peek.AsMemory(), SocketFlags.Peek, timeout.Token) == 0)
                        return;
                    if (peek[0] is (byte)'G' or (byte)'P' or (byte)'H' or (byte)'D' or (byte)'O' or (byte)'T')
                    {
                        var clearHead = await HttpWire.ReadHeadAsync(stream, timeout.Token);
                        if (clearHead is null)
                            return;
                        await ForwardAsync(stream, clearHead, new UriBuilder("http", destination.Host, destination.Port).Uri, cancellationToken);
                        return;
                    }
                    if (!decryptHttps)
                    {
                        Captured?.Invoke(new CaptureRecord { Method = "CONNECT", Url = destination.ToString(), Response = new HttpResult { StatusCode = 200, StatusText = "TLS 隧道", Body = "已建立 TLS 隧道。启用 HTTPS 解密并信任本地 CA 后可查看内容。" } });
                        using var remote = new TcpClient();
                        await remote.ConnectAsync(destination.Host, destination.Port, timeout.Token);
                        await TunnelAsync(stream, remote.GetStream(), cancellationToken);
                        return;
                    }
                    await using var ssl = new SslStream(stream, true);
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificates.ForHost(destination.Host), EnabledSslProtocols = SslProtocols.None, ApplicationProtocols = [SslApplicationProtocol.Http11] }, timeout.Token);
                    var inner = await HttpWire.ReadHeadAsync(ssl, timeout.Token);
                    if (inner is null)
                        return;
                    await ForwardAsync(ssl, inner, destination, cancellationToken);
                }
                else
                    await ForwardAsync(stream, head, null, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or HttpRequestException or AuthenticationException or OperationCanceledException or UriFormatException or InvalidOperationException or FormatException)
        {
            if (!cancellationToken.IsCancellationRequested)
                Captured?.Invoke(new CaptureRecord { Method = "ERROR", Url = "代理连接", Response = new HttpResult { Error = exception.Message, Body = exception.Message } });
        }
        finally { _connections.TryRemove(client, out _); completion.SetResult(); }
    }

    private async Task ForwardAsync(Stream downstream, WireHead head, Uri? tunnel, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var parts = head.StartLine.Split(' ');
        var uri = Uri.TryCreate(parts[1], UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" ? absolute : new Uri(tunnel ?? new Uri("http://" + head.Header("Host")), parts[1]);
        if (head.Header("Upgrade")?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true)
        {
            await ForwardWebSocketAsync(downstream, head, uri, cancellationToken);
            return;
        }
        if (head.Header("Expect")?.Equals("100-continue", StringComparison.OrdinalIgnoreCase) == true)
            await HttpWire.WriteAsync(downstream, "HTTP/1.1 100 Continue\r\n\r\n", cancellationToken);
        var body = await HttpWire.ReadBodyAsync(downstream, head, cancellationToken);
        using var request = new HttpRequestMessage(new HttpMethod(parts[0]), uri) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
        if (body.Length > 0)
            request.Content = new ByteArrayContent(body);
        foreach (var header in head.Headers.Where(x => !HopHeaders.Contains(x.Key) && !x.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseHeaders = response.Headers.Concat(response.Content.Headers).Where(x => !HopHeaders.Contains(x.Key)).Select(x => x.Key + ": " + string.Join(", ", x.Value)).ToArray();
        await HttpWire.WriteAsync(downstream, $"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n{string.Join("\r\n", responseHeaders)}\r\nConnection: close\r\n\r\n", cancellationToken);
        await using var upstream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var preview = new MemoryStream();
        var buffer = new byte[32768];
        long size = 0;
        if (parts[0] != "HEAD")
            while (true)
            {
                var count = await upstream.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                    break;
                await downstream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                size += count;
                if (preview.Length < 1024 * 1024)
                    preview.Write(buffer, 0, (int)Math.Min(count, 1024 * 1024 - preview.Length));
            }
        await downstream.FlushAsync(cancellationToken);
        Captured?.Invoke(new CaptureRecord { Method = parts[0], Url = uri.ToString(), RequestHeaders = head.Display, RequestBody = Encoding.UTF8.GetString(body), Response = new HttpResult { StatusCode = (int)response.StatusCode, StatusText = response.ReasonPhrase ?? "", Headers = string.Join('\n', responseHeaders), Body = Encoding.UTF8.GetString(preview.ToArray()), DurationMs = timer.ElapsedMilliseconds, Size = size, Truncated = size > preview.Length } });
    }

    private async Task ForwardWebSocketAsync(Stream downstream, WireHead request, Uri uri, CancellationToken cancellationToken)
    {
        using var remote = new TcpClient();
        await remote.ConnectAsync(uri.Host, uri.Port, cancellationToken);
        await using var network = remote.GetStream();
        SslStream? tls = null;
        Stream upstream = network;
        if (uri.Scheme == "https")
        {
            tls = new SslStream(network, true);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = uri.Host, ApplicationProtocols = [SslApplicationProtocol.Http11] }, cancellationToken);
            upstream = tls;
        }
        try
        {
            await HttpWire.WriteAsync(upstream, $"GET {uri.PathAndQuery} HTTP/1.1\r\n{string.Join("\r\n", request.Headers.Where(x => !x.Key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) && !x.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)).Select(x => x.Key + ": " + x.Value))}\r\n\r\n", cancellationToken);
            var response = await HttpWire.ReadHeadAsync(upstream, cancellationToken) ?? throw new IOException("WebSocket 握手失败。");
            await HttpWire.WriteAsync(downstream, response.StartLine + "\r\n" + string.Join("\r\n", response.Headers.Select(x => x.Key + ": " + x.Value)) + "\r\n\r\n", cancellationToken);
            var status = int.Parse(response.StartLine.Split(' ')[1]);
            Captured?.Invoke(new CaptureRecord { Method = "WS", Url = uri.ToString(), RequestHeaders = request.Display, Response = new HttpResult { StatusCode = status, Headers = response.Display } });
            if (status != 101)
                return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var outbound = FramesAsync(downstream, upstream, uri.ToString(), "↑", linked.Token);
            var inbound = FramesAsync(upstream, downstream, uri.ToString(), "↓", linked.Token);
            var finished = await Task.WhenAny(outbound, inbound);
            if (!finished.IsFaulted)
            {
                try
                {
                    await Task.WhenAll(outbound, inbound).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (TimeoutException) { }
            }
            await linked.CancelAsync();
            try
            {
                await Task.WhenAll(outbound, inbound);
            }
            catch (OperationCanceledException) { }
        }
        finally { if (tls is not null) await tls.DisposeAsync(); }
    }

    private async Task FramesAsync(Stream source, Stream destination, string url, string direction, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var first = new byte[2];
            var count = await source.ReadAsync(first.AsMemory(0, 1), cancellationToken);
            if (count == 0)
                return;
            await source.ReadExactlyAsync(first.AsMemory(1, 1), cancellationToken);
            using var wire = new MemoryStream();
            wire.Write(first);
            ulong length = (ulong)(first[1] & 127);
            if (length is 126 or 127)
            {
                var extended = new byte[length == 126 ? 2 : 8];
                await source.ReadExactlyAsync(extended, cancellationToken);
                wire.Write(extended);
                length = 0;
                foreach (var b in extended)
                    length = (length << 8) | b;
            }
            if (length > 16 * 1024 * 1024)
                throw new IOException("WebSocket 帧超过 16 MB。");
            byte[] mask = (first[1] & 128) != 0 ? new byte[4] : [];
            if (mask.Length > 0)
            {
                await source.ReadExactlyAsync(mask, cancellationToken);
                wire.Write(mask);
            }
            var payload = new byte[(int)length];
            await source.ReadExactlyAsync(payload, cancellationToken);
            wire.Write(payload);
            await destination.WriteAsync(wire.ToArray(), cancellationToken);
            await destination.FlushAsync(cancellationToken);
            for (var i = 0; i < payload.Length && mask.Length > 0; i++)
                payload[i] ^= mask[i % 4];
            var opcode = first[0] & 15;
            var compressed = (first[0] & 64) != 0;
            var content = opcode == 1 && !compressed ? Encoding.UTF8.GetString(payload) : Convert.ToBase64String(payload);
            if (content.Length > 65536)
                content = content[..65536] + "…[截断]";
            Captured?.Invoke(new CaptureRecord { Method = "WS " + direction, Url = url, Response = new HttpResult { StatusCode = 101, StatusText = $"opcode={opcode}" + (compressed ? " compressed" : ""), Body = content, Size = payload.Length } });
            if (opcode == 8)
                return;
        }
    }

    private static async Task TunnelAsync(Stream left, Stream right, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var a = left.CopyToAsync(right, linked.Token);
        var b = right.CopyToAsync(left, linked.Token);
        await Task.WhenAny(a, b);
        await linked.CancelAsync();
        try
        {
            await Task.WhenAll(a, b);
        }
        catch (OperationCanceledException) { }
    }

    public async Task StopAsync()
    {
        var lifetime = _lifetime;
        if (lifetime is null)
            return;
        var listener = _listener;
        _listener = null;
        listener?.Stop();
        await lifetime.CancelAsync();
        foreach (var connection in _connections.Keys)
            connection.Dispose();
        if (_acceptLoop is not null)
            await _acceptLoop;
        await Task.WhenAll(_connections.Values);
        lifetime.Dispose();
        _lifetime = null;
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client.Dispose();
        _certificates.Dispose();
    }
}
