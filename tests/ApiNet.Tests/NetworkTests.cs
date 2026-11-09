using System.Net;
using System.Net.WebSockets;
using System.Text;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Http;
using ApiNet.Infrastructure.WebSockets;
using ApiNet.Proxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ApiNet.Proxy.Certificates;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
namespace ApiNet.Tests;

public class NetworkTests
{
    internal static async Task<WebApplication> StartServerAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/get", () => Results.Text("hello-network"));
        app.MapGet("/download", () => Results.File(Enumerable.Range(0, HttpRequestService.MaximumBodyBytes + 131072).Select(x => (byte)(x % 251)).ToArray(), "application/zip", "../../report.zip"));
        app.MapPost("/inspect", context => context.Response.WriteAsync(context.Request.ContentType + "\n" + context.Request.Query["search"]));
        app.MapPost("/echo", async context => { context.Response.ContentType = "application/json"; await context.Request.Body.CopyToAsync(context.Response.Body); });
        app.MapGet("/slow", async context => { await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted); await context.Response.WriteAsync("late"); });
        app.Map("/ws", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[8192];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }
                await socket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
            }
        });
        await app.StartAsync();
        return app;
    }
    internal static string Address(WebApplication server) => server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    [Fact]
    public async Task FileDownloadKeepsEveryByteBeyondPreviewLimitAndOmitsTempPathFromHistory()
    {
        await using var server = await StartServerAsync();
        var result = await new HttpRequestService().SendAsync(new RequestDefinition { Url = Address(server) + "/download" }, CancellationToken.None);
        try
        {
            Assert.Null(result.Error);
            Assert.Equal("report.zip", result.FileName);
            Assert.Equal(HttpRequestService.MaximumBodyBytes + 131072, result.Size);
            Assert.NotNull(result.DownloadPath);
            var bytes = await File.ReadAllBytesAsync(result.DownloadPath);
            Assert.Equal(result.Size, bytes.Length);
            Assert.True(bytes.Select((value, index) => value == (byte)(index % 251)).All(x => x));
            var history = HistorySanitizer.Create(new RequestDefinition(), result, new WorkspaceDocument());
            Assert.Null(history.Response.DownloadPath);
            Assert.Equal("application/zip", history.Response.ContentType);
        }
        finally { if (result.DownloadPath is not null) File.Delete(result.DownloadPath); }
    }
    [Fact]
    public async Task XmlBodyAndVariableQueryUseSelectedMimeAndCorrectEscaping()
    {
        await using var server = await StartServerAsync();
        var request = new RequestDefinition { Method = "POST", Url = Address(server) + "/inspect#fragment", BodyFormat = "XML", Body = "<hello />", QueryParameters = [new() { Key = "search", Value = "{{term}}" }, new() { Key = "ignored", Value = "{{missing}}", Enabled = false }] };
        var resolved = new ApiNet.Infrastructure.Variables.VariableResolver().ResolveRequest(request, new WorkspaceDocument { SharedVariables = [new() { Key = "term", Value = "汉字 &+?=" }] });
        var result = await new HttpRequestService().SendAsync(resolved, CancellationToken.None);
        try
        {
            Assert.Null(result.Error);
            Assert.Equal("application/xml; charset=utf-8\n汉字 &+?=", result.Body);
        }
        finally { if (result.DownloadPath is not null) File.Delete(result.DownloadPath); }
    }
    [Fact]
    public async Task HttpExecutesRealPostAndRecordsResponse()
    {
        await using var server = await StartServerAsync();
        var result = await new HttpRequestService().SendAsync(new RequestDefinition { Method = "POST", Url = Address(server) + "/echo", Body = "{\"hello\":\"world\"}", Headers = "Content-Type: application/json" }, CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Equal(200, result.StatusCode);
        Assert.Contains("world", result.Body);
        Assert.True(result.Size > 0);
    }
    [Fact]
    public async Task CancellationInterruptsInFlightRequest()
    {
        await using var server = await StartServerAsync();
        using var timeout = new CancellationTokenSource(150);
        var result = await new HttpRequestService().SendAsync(new RequestDefinition { Url = Address(server) + "/slow" }, timeout.Token);
        Assert.Equal("请求已取消", result.Error);
    }
    [Fact]
    public async Task SocketReceivesEchoAndDisconnects()
    {
        await using var server = await StartServerAsync();
        await using var session = new WebSocketSession();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.MessageReceived += message => { if (message.Direction == "接收") received.TrySetResult(message.Content); };
        await session.ConnectAsync(new RequestDefinition { Url = Address(server).Replace("http://", "ws://") + "/ws" }, CancellationToken.None);
        await session.SendAsync("echo-test", false, CancellationToken.None);
        Assert.Equal("echo-test", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await session.DisconnectAsync();
        Assert.False(session.IsConnected);
    }
    [Fact]
    public async Task ForwardProxyCapturesRealHttpTraffic()
    {
        await using var server = await StartServerAsync();
        var directory = Path.Combine(Path.GetTempPath(), "apinet-proxy-" + Guid.NewGuid().ToString("N"));
        await using var proxy = new ForwardProxyService(directory);
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var captured = new TaskCompletionSource<CaptureRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.Captured += capture => captured.TrySetResult(capture);
        await proxy.StartAsync(port, false);
        using var handler = new HttpClientHandler { Proxy = new WebProxy("http://127.0.0.1:" + port), UseProxy = true };
        using var client = new HttpClient(handler);
        Assert.Equal("hello-network", await client.GetStringAsync(Address(server) + "/get"));
        var record = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(200, record.Response.StatusCode);
        Assert.Equal("hello-network", record.Response.Body);
        await proxy.StopAsync();
        Assert.False(proxy.IsRunning);
        await proxy.StartAsync(port, false);
        using var restartedClient = new HttpClient(new HttpClientHandler { Proxy = new WebProxy("http://127.0.0.1:" + port), UseProxy = true });
        Assert.Equal("hello-network", await restartedClient.GetStringAsync(Address(server) + "/get"));
    }
    [Fact]
    public async Task HttpsProxyDecryptsWithTrustedLocalCa()
    {
        var originDirectory = Path.Combine(Path.GetTempPath(), "apinet-origin-" + Guid.NewGuid().ToString("N"));
        using var originCa = new CertificateAuthority(originDirectory);
        using var originRoot = X509CertificateLoader.LoadCertificateFromFile(originCa.ExportPublicCertificate());
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(originCa.ForHost("127.0.0.1"))));
        await using var server = builder.Build();
        server.MapGet("/secure", () => "decrypted-https");
        await server.StartAsync();
        var proxyDirectory = Path.Combine(Path.GetTempPath(), "apinet-tls-proxy-" + Guid.NewGuid().ToString("N"));
        var upstream = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, cert, _, errors) => Trust(cert, originRoot, errors) };
        await using var proxy = new ForwardProxyService(proxyDirectory, upstream);
        using var proxyRoot = X509CertificateLoader.LoadCertificateFromFile(proxy.ExportCertificate());
        var port = FreePort();
        var captured = new TaskCompletionSource<CaptureRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.Captured += record => captured.TrySetResult(record);
        await proxy.StartAsync(port, true);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = true, Proxy = new WebProxy("http://127.0.0.1:" + port), ServerCertificateCustomValidationCallback = (_, cert, _, errors) => Trust(cert, proxyRoot, errors) });
        string body;
        try
        {
            body = await client.GetStringAsync(Address(server) + "/secure");
        }
        catch (Exception exception) { var failure = await captured.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Fail($"{exception.Message}\nProxy: {failure.Response.Error}"); return; }
        Assert.Equal("decrypted-https", body);
        Assert.Equal("decrypted-https", (await captured.Task.WaitAsync(TimeSpan.FromSeconds(5))).Response.Body);
    }
    [Fact]
    public async Task WebSocketProxyForwardsAndCapturesFrames()
    {
        await using var server = await StartServerAsync();
        await using var proxy = new ForwardProxyService(Path.Combine(Path.GetTempPath(), "apinet-ws-proxy-" + Guid.NewGuid().ToString("N")));
        var port = FreePort();
        var captured = new TaskCompletionSource<CaptureRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.Captured += record => { if (record.Method == "WS ↓") captured.TrySetResult(record); };
        await proxy.StartAsync(port, false);
        using var client = new ClientWebSocket();
        client.Options.Proxy = new WebProxy("http://127.0.0.1:" + port);
        await client.ConnectAsync(new Uri(Address(server).Replace("http://", "ws://") + "/ws"), CancellationToken.None);
        var payload = Encoding.UTF8.GetBytes("frame-test");
        await client.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[128];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Assert.Equal("frame-test", Encoding.UTF8.GetString(buffer, 0, result.Count));
        Assert.Equal("frame-test", (await captured.Task.WaitAsync(TimeSpan.FromSeconds(5))).Response.Body);
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }
    private static bool Trust(X509Certificate2? certificate, X509Certificate2 root, SslPolicyErrors errors)
    {
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }
    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
