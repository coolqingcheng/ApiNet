using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Desktop;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Settings;
using ApiNet.Desktop.Views;
using ApiNet.Desktop.Views.Settings;
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
using ApiNet.Desktop.ViewModels.Tabs;
using ApiNet.Desktop.ViewModels.Dialogs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using Avalonia.VisualTree;
using ApiNet.Desktop.Controls;
using ApiNet.Desktop.Views.Tabs;

[assembly: AvaloniaTestApplication(typeof(ApiNet.Desktop.Tests.TestAppBuilder))]
namespace ApiNet.Desktop.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public class WorkbenchTests
{
    private static MainWindowViewModel CreateModel(TestDialogs? dialogs = null)
    {
        var store = new SqliteWorkspaceStore(Path.Combine(Path.GetTempPath(), "apinet-ui-" + Guid.NewGuid().ToString("N")));
        return new MainWindowViewModel(store, new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(store.DataDirectory), new VelopackUpdateService(), dialogs ?? new TestDialogs());
    }
    [AvaloniaFact]
    public async Task WorkbenchRendersAndTabStateIsIndependent()
    {
        var model = CreateModel();
        await model.InitializeAsync();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.UseWebSockets();
        server.MapGet("/health", () => Results.Text("{\n  \"status\": \"ok\",\n  \"service\": \"ApiNet local test server\",\n  \"version\": \"0.1.0\"\n}", "application/json"));
        server.Map("/ws", async context => { using var socket = await context.WebSockets.AcceptWebSocketAsync(); var buffer = new byte[4096]; var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None); await socket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, true, CancellationToken.None); var close = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None); if (close.MessageType == WebSocketMessageType.Close) await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); });
        await server.StartAsync();
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var window = new MainWindow { DataContext = model };
        window.Show();
        Assert.Single(model.Tabs);
        var first = model.SelectedTab!;
        first.Url = "https://first.example.com";
        await model.NewHttpCommand.ExecuteAsync(null);
        var second = model.SelectedTab!;
        second.Url = "https://second.example.com";
        model.SelectedTab = first;
        Assert.Equal("https://first.example.com", first.Url);
        Assert.Equal("https://second.example.com", second.Url);
        Assert.True(window.GetVisualDescendants().OfType<TabControl>().Single(x => x.Name == "RequestTabs").Items.Count >= 2);
        model.Workspace.Environments[0].Variables.First(x => x.Key == "baseUrl").Value = address;
        first.Url = "{{baseUrl}}/health";
        first.Name = "服务健康检查";
        first.AddQueryCommand.Execute(null);
        first.QueryRows[0].Key = "source";
        first.QueryRows[0].Value = "{{baseUrl}}";
        await ((HttpRequestTabViewModel)first).SendCommand.ExecuteAsync(null);
        Assert.Contains("200", first.Status);
        Assert.Contains("ApiNet local test server", first.ResponseBody);
        SaveFrame(window, "workbench.png");
        first.Body = "{\n  \"host\": \"{{baseUrl}}\",\n  \"enabled\": true,\n  \"count\": 42\n}";
        window.GetVisualDescendants().OfType<HttpRequestView>().Single().GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 2;
        SaveFrame(window, "body-editor.png");
        model.OpenRequest(model.Workspace.Requests.Single(x => x.Protocol == "WebSocket"));
        var ws = (WebSocketTabViewModel)model.SelectedTab!;
        Assert.False(ws.IsDirty, $"Method=[{ws.Method}] URL=[{ws.Url}] Headers=[{ws.Headers ?? "NULL"}] Body=[{ws.Body ?? "NULL"}] Subprotocol=[{ws.Subprotocol ?? "NULL"}] Name=[{ws.Name}] Query=[{ws.Query ?? "NULL"}] Token=[{ws.BearerToken ?? "NULL"}]");
        ws.Url = address.Replace("http://", "ws://") + "/ws";
        await ws.ConnectCommand.ExecuteAsync(null);
        ws.Body = "Hello, ApiNet";
        await ws.SendMessageCommand.ExecuteAsync(null);
        for (var i = 0; i < 50 && !ws.Messages.Any(x => x.Direction == "接收"); i++)
            await Task.Delay(20);
        Assert.Contains(ws.Messages, x => x.Direction == "接收" && x.Content == "Hello, ApiNet");
        SaveFrame(window, "websocket.png");
        model.ActivePanel = "历史记录";
        SaveFrame(window, "history.png");
        model.ActivePanel = "代理抓包";
        SaveFrame(window, "capture.png");
        await model.ShutdownAsync();
        window.Close();
    }
    [AvaloniaFact]
    public async Task FileResponsePromptsForLocalSaveWithoutSendingAgain()
    {
        var destination = Path.Combine(Path.GetTempPath(), "apinet-save-" + Guid.NewGuid().ToString("N") + ".bin");
        var dialogs = new TestDialogs { ResponseDestination = destination };
        var model = CreateModel(dialogs);
        await model.InitializeAsync();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var calls = 0;
        byte[] expected = [0, 255, 1, 2, 3, 128];
        server.MapGet("/file", () => { calls++; return Results.File(expected, "application/octet-stream", "sample.bin"); });
        await server.StartAsync();
        var tab = (HttpRequestTabViewModel)model.SelectedTab!;
        tab.Url = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/file";
        try
        {
            await tab.SendCommand.ExecuteAsync(null);
            Assert.Equal(1, calls);
            Assert.Equal(1, dialogs.ResponsePrompts);
            Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
            Assert.Contains("已保存", tab.Status);
        }
        finally { await model.ShutdownAsync(); File.Delete(destination); }
    }
    [AvaloniaFact]
    public async Task ParameterRowsPersistDisabledValuesAndKeepSaveStateClean()
    {
        var model = CreateModel();
        await model.InitializeAsync();
        var tab = model.SelectedTab!;
        tab.AddHeaderCommand.Execute(null);
        var header = tab.HeaderRows.Last();
        header.Key = "X-Example";
        header.Value = "{{missing}}";
        header.Enabled = false;
        tab.AddQueryCommand.Execute(null);
        tab.QueryRows[0].Key = "page";
        tab.QueryRows[0].Value = "2";
        tab.BodyFormat = "XML";
        tab.Body = "<root><host>{{baseUrl}}</host></root>";
        Assert.True(tab.IsDirty);
        await tab.SaveCommand.ExecuteAsync(null);
        Assert.False(tab.IsDirty);
        var saved = model.Workspace.Requests.Single(x => x.Id == tab.Id);
        Assert.Equal("XML", saved.BodyFormat);
        Assert.False(saved.HeaderParameters.Single().Enabled);
        Assert.DoesNotContain("X-Example", tab.Resolve().Headers);
        Assert.Contains("page=2", tab.Resolve().Url);
        Assert.Equal("{{missing}}", saved.HeaderParameters.Single().Value);
        tab.HeaderRows.Single().RemoveCommand.Execute(null);
        Assert.Empty(tab.HeaderRows);
        Assert.True(tab.IsDirty);
        tab.BearerToken = "{{baseUrl}}";
        await tab.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Authorization", Assert.Single(tab.HeaderRows).Key);
        Assert.Equal("Bearer {{baseUrl}}", tab.HeaderRows[0].Value);
        Assert.False(tab.IsDirty);
        await model.ShutdownAsync();
    }
    [AvaloniaFact]
    public void HighlightingEditorEditsBothWaysAndRendersVariablesAndXml()
    {
        var editor = new CodeEditor { Text = "<host>{{baseUrl}}</host>", Language = "XML" };
        var window = new Window { Width = 640, Height = 240, Content = editor };
        window.Show();
        Assert.Equal(editor.Text, editor.Editor.Text);
        editor.Editor.Text = "<host>{{api.host}}</host>";
        Assert.Equal("<host>{{api.host}}</host>", editor.Text);
        Assert.Matches(SyntaxColorizer.Variables(), editor.Text);
        SaveFrame(window, "xml-editor.png");
        window.Close();
    }
    [AvaloniaFact]
    public async Task CancelClosingDirtyTabKeepsItsDraft()
    {
        var model = CreateModel();
        await model.InitializeAsync();
        var tab = model.SelectedTab!;
        tab.Body = "unsaved";
        Assert.False(await model.CloseTabAsync(tab));
        Assert.Contains(tab, model.Tabs);
        Assert.Equal("unsaved", tab.Body);
        await model.ShutdownAsync();
    }
    [AvaloniaFact]
    public void EnvironmentAndSettingsViewsRender()
    {
        var workspace = new WorkspaceDocument { SharedVariables = [new() { Key = "baseUrl", Value = "http://localhost:5000" }, new() { Key = "token", Value = "sample", IsSecret = true }] };
        var editor = new EnvironmentEditorViewModel(workspace);
        var environment = new EnvironmentWindow(editor);
        environment.Show();
        SaveFrame(environment, "environments.png");
        environment.Close();
        var settings = new SettingsWindow(new SettingsViewModel(workspace.Settings));
        settings.Show();
        SaveFrame(settings, "settings.png");
        settings.Close();
    }
    [AvaloniaFact]
    public async Task MovingFolderIntoItsDescendantIsRejected()
    {
        var model = CreateModel();
        await model.InitializeAsync();
        var parent = model.Workspace.Folders[0];
        var child = new FolderDefinition { ParentId = parent.Id };
        model.Workspace.Folders.Add(child);
        await model.MoveNodeAsync(parent.Id, child.Id);
        Assert.Null(parent.ParentId);
        await model.ShutdownAsync();
    }
    [AvaloniaFact]
    public async Task HistoryFiltersAndRestoresWithoutSending()
    {
        var model = CreateModel();
        await model.InitializeAsync();
        var get = new HistoryEntry { EnvironmentName = "测试", FolderName = "用户", Request = new RequestDefinition { Name = "读取用户", Method = "GET", Url = "https://example.com/users" }, Response = new HttpResult { StatusCode = 200, Body = "saved-response" } };
        await model.RecordHistoryAsync(get);
        await model.RecordHistoryAsync(new HistoryEntry { Request = new RequestDefinition { Method = "POST" }, Response = new HttpResult { StatusCode = 500 } });
        model.HistoryMethod = "GET";
        Assert.Single(model.History);
        model.HistorySearch = "用户";
        Assert.Single(model.History);
        model.HistoryResult = "HTTP 错误";
        Assert.Empty(model.History);
        model.HistoryResult = "成功";
        model.SelectedHistory = get;
        await model.RestoreHistoryCommand.ExecuteAsync(null);
        Assert.Equal("saved-response", model.SelectedTab!.ResponseBody);
        Assert.True(model.SelectedTab.IsDirty);
        Assert.False(model.SelectedTab.IsBusy);
        await model.ShutdownAsync();
    }
    private static void SaveFrame(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var root = Environment.GetEnvironmentVariable("APINET_SCREENSHOT_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(root);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(root, name));
    }
    private sealed class TestDialogs : IDialogService
    {
        public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => Task.FromResult<ImportSelection?>(null);
        public string? ResponseDestination { get; init; }
        public int ResponsePrompts { get; private set; }
        public Task<string?> PromptAsync(string title, string initial = "") => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<int> ChooseAsync(string title, string message, string[] choices) => Task.FromResult(-1);
        public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenUploadFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
        public Task<string?> SaveResponseAsync(string name) { ResponsePrompts++; return Task.FromResult(ResponseDestination); }
        public Task EditEnvironmentsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
        public Task EditSettingsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
    }
}
