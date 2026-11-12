using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Desktop.Controls;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Dialogs;
using ApiNet.Desktop.ViewModels.Tabs;
using ApiNet.Desktop.Views;
using ApiNet.Desktop.Views.Dialogs;
using ApiNet.Infrastructure.Http;
using ApiNet.Infrastructure.OpenApi;
using ApiNet.Infrastructure.Persistence;
using ApiNet.Infrastructure.Variables;
using ApiNet.Proxy;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ApiNet.Desktop.Tests;

public partial class UiFeedbackTests
{
    private static MainWindowViewModel CreateModel(TestUpdates? updates = null, TestDialogs? dialogs = null, IHttpRequestService? http = null, SqliteWorkspaceStore? store = null)
    {
        store ??= new SqliteWorkspaceStore(Path.Combine(Path.GetTempPath(), "apinet-feedback-" + Guid.NewGuid().ToString("N")));
        return new(store, new VariableResolver(), http ?? new HttpRequestService(), new OpenApiService(), new ForwardProxyService(store.DataDirectory), updates ?? new TestUpdates(), dialogs ?? new TestDialogs());
    }
    [AvaloniaFact]
    public async Task UrlAndQueryTableSynchronizeDuplicatesVariablesDisabledRowsAndSavedRequests()
    {
        var model = CreateModel(); await model.InitializeAsync();
        try
        {
            var tab = model.SelectedTab!;
            tab.Url = "https://example.test/search?tag=a&tag=b&empty=&flag&text=%E4%B8%AD%E6%96%87+value&next=a%26b%3Dc&host={{baseUrl}}#results";
            Assert.Equal(7, tab.QueryRows.Count); Assert.Equal("中文 value", tab.QueryRows[4].Value); Assert.Equal("a&b=c", tab.QueryRows[5].Value);
            Assert.Equal("", tab.QueryRows[3].Value); Assert.Equal("{{baseUrl}}", tab.QueryRows[6].Value);
            tab.QueryRows[0].Value = "汉字 &+=?";
            Assert.Contains("tag=%E6%B1%89%E5%AD%97%20%26%2B%3D%3F", tab.Url); Assert.Contains("host={{baseUrl}}", tab.Url); Assert.EndsWith("#results", tab.Url);
            var second = tab.QueryRows[1]; second.Enabled = false;
            var resolved = QueryUrl.Parse(tab.Resolve().Url);
            Assert.Single(resolved, x => x.Key == "tag"); Assert.Equal("https://httpbin.org", resolved.Single(x => x.Key == "host").Value);
            Assert.DoesNotContain('?', tab.Snapshot().Url);
            tab.Url = "https://example.test/new?page=2#part";
            Assert.Contains(second, tab.QueryRows); Assert.Single(tab.QueryRows, x => x.Enabled); Assert.Equal("page", tab.QueryRows[0].Key);
            tab.QueryRows[0].Value = "3"; second.RemoveCommand.Execute(null);
            Assert.Equal("https://example.test/new?page=3#part", tab.Url);
            await tab.SaveCommand.ExecuteAsync(null); Assert.False(tab.IsDirty);
            var saved = model.Workspace.Requests.Single(x => x.Id == tab.Id);
            Assert.Single(saved.QueryParameters); Assert.Equal("https://example.test/new#part", saved.Url);
            await tab.CloseCommand.ExecuteAsync(null); model.OpenRequest(saved);
            Assert.Equal("https://example.test/new?page=3#part", model.SelectedTab!.Url); Assert.False(model.SelectedTab.IsDirty);
            Assert.Single(QueryUrl.Parse(model.SelectedTab.Resolve().Url));
            var legacy = new RequestDefinition { Url = "https://example.test/?a=1", QueryParameters = [new() { Key = "a", Value = "2" }] };
            model.Workspace.Requests.Add(legacy); model.OpenRequest(legacy);
            Assert.False(model.SelectedTab.IsDirty); Assert.Equal(2, QueryUrl.Parse(model.SelectedTab.Resolve().Url).Count);
        }
        finally { await model.ShutdownAsync(); }
    }
    [AvaloniaFact]
    public async Task SchemeLessUrlPreviewSendAndHistoryUseHttpWhileSavedInputStaysEditable()
    {
        var http = new RecordingHttp();
        var model = CreateModel(http: http); await model.InitializeAsync();
        try
        {
            model.Workspace.SharedVariables.Add(new() { Key = "host", Value = "localhost:8080" });
            var tab = Assert.IsType<HttpRequestTabViewModel>(model.SelectedTab);
            tab.Url = "{{host}}/users?name=one#part";
            tab.QueryRows.Single().Value = "中文 &+";
            var expected = "http://localhost:8080/users?name=%E4%B8%AD%E6%96%87%20%26%2B#part";
            tab.PreviewCommand.Execute(null); Assert.Equal(expected, tab.ResolvedUrl);
            await tab.SendCommand.ExecuteAsync(null);
            Assert.Equal(expected, http.Request!.Url);
            Assert.Equal(expected, Assert.Single(model.History).Request.Url);
            Assert.StartsWith("{{host}}/users?", tab.Url);
            await tab.SaveCommand.ExecuteAsync(null);
            Assert.False(tab.IsDirty);
            var saved = model.Workspace.Requests.Single(request => request.Id == tab.Id);
            Assert.Equal("{{host}}/users#part", saved.Url);
            Assert.Equal("中文 &+", saved.QueryParameters.Single().Value);
        }
        finally { await model.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task UploadFilePickerAndTablePersistFileTypesAndPaths()
    {
        var path = Path.GetTempFileName();
        var dialogs = new TestDialogs { UploadPath = path };
        var model = CreateModel(dialogs: dialogs); await model.InitializeAsync();
        var tab = model.SelectedTab!; tab.Method = "POST"; tab.BodyFormat = "Multipart";
        tab.AddBodyParameterCommand.Execute(null); var file = tab.BodyRows[0]; file.Key = "document"; file.Type = "File"; file.Enabled = false;
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            await file.BrowseFileCommand.ExecuteAsync(null);
            Assert.True(file.Enabled); Assert.Equal(path, file.Value); Assert.True(file.IsFile);
            tab.AddBodyParameterCommand.Execute(null); tab.BodyRows[1].Key = "description"; tab.BodyRows[1].Value = "{{baseUrl}}";
            window.UpdateLayout();
            var view = window.GetVisualDescendants().OfType<ApiNet.Desktop.Views.Tabs.HttpRequestView>().Single();
            view.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 2;
            SaveFrame(window, "multipart-table.png");
            var table = view.GetVisualDescendants().OfType<ParameterTable>().Single(x => x.AllowFiles);
            Assert.True(table.IsVisible); Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "KEY");
            await tab.SaveCommand.ExecuteAsync(null); Assert.False(tab.IsDirty);
            var saved = model.Workspace.Requests.Single(x => x.Id == tab.Id);
            Assert.Equal(path, saved.BodyParameters[0].Value); Assert.Equal("File", saved.BodyParameters[0].Type);
            dialogs.UploadPath = null; await file.BrowseFileCommand.ExecuteAsync(null); Assert.Equal(path, file.Value);
            await tab.CloseCommand.ExecuteAsync(null); model.OpenRequest(saved);
            Assert.True(model.SelectedTab!.BodyRows[0].IsFile); Assert.Equal(path, model.SelectedTab.BodyRows[0].Value);
        }
        finally { await model.ShutdownAsync(); window.Close(); File.Delete(path); }
    }
    [AvaloniaFact]
    public async Task LightThemeChangesFocusedInputsEditorsAndTablesAndPersistsSelection()
    {
        var store = new SqliteWorkspaceStore(Path.Combine(Path.GetTempPath(), "apinet-theme-" + Guid.NewGuid().ToString("N")));
        var model = CreateModel(store: store); await model.InitializeAsync();
        var tab = model.SelectedTab!; tab.Url = "https://example.test/search?name={{baseUrl}}&page=1";
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            await model.ToggleThemeCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Light", model.Workspace.Settings.Theme); Assert.Equal(Avalonia.Styling.ThemeVariant.Light, window.ActualThemeVariant);
            var name = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "RequestName"); name.Focus(); Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "light-theme.png");
            var border = name.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "PART_BorderElement");
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
            Assert.Equal(Color.Parse("#202735"), Assert.IsAssignableFrom<ISolidColorBrush>(name.Foreground).Color);
            var editor = window.GetVisualDescendants().OfType<CodeEditor>().Single(x => x.SingleLine && x.Text == tab.Url);
            Assert.Equal(Color.Parse("#253046"), Assert.IsAssignableFrom<ISolidColorBrush>(editor.Editor.Foreground).Color);
            editor.Editor.TextArea.TextView.EnsureVisualLines();
            Assert.Contains(editor.Editor.TextArea.TextView.VisualLines.SelectMany(x => x.Elements), x => x.TextRunProperties.ForegroundBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#A74412"));
            await model.ShutdownAsync();
            Assert.Equal("Light", (await store.LoadAsync())!.Settings.Theme);
            var settings = new ApiNet.Desktop.ViewModels.Settings.SettingsViewModel(model.Workspace.Settings); Assert.Equal("浅色", settings.Theme);
            await model.ToggleThemeCommand.ExecuteAsync(null); SaveFrame(window, "parameter-table.png");
        }
        finally { ThemeService.Apply("Dark"); await model.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ToolsRenderTransformAndKeepTheRequestTab()
    {
        var model = CreateModel(); await model.InitializeAsync();
        var tab = model.SelectedTab; var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            model.NavigateCommand.Execute("工具"); model.Tools.Input = "{\"message\":\"你好\",\"ok\":true}";
            await model.Tools.TransformCommand.ExecuteAsync("format"); Assert.Contains('\n', model.Tools.Output); Assert.False(model.ShowRequests);
            SaveFrame(window, "tools-dark.png");
            await model.ToggleThemeCommand.ExecuteAsync(null); SaveFrame(window, "tools-light.png");
            model.Tools.Input = "bad json"; await model.Tools.TransformCommand.ExecuteAsync("format"); Assert.Contains("处理失败", model.Tools.Status);
            model.Tools.SelectedTool = "URL 编码 / 解码"; model.Tools.Input = "中文 &+=?"; await model.Tools.TransformCommand.ExecuteAsync("encode");
            Assert.Equal("%E4%B8%AD%E6%96%87%20%26%2B%3D%3F", model.Tools.Output);
            model.Tools.UseOutputCommand.Execute(null); await model.Tools.TransformCommand.ExecuteAsync("decode"); Assert.Equal("中文 &+=?", model.Tools.Output);
            model.NavigateCommand.Execute("工作区"); Assert.Same(tab, model.SelectedTab); Assert.True(model.ShowRequests);
        }
        finally { ThemeService.Apply("Dark"); await model.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task FocusedInputsAndRealUrlRunsRenderWithReadableDarkThemeColors()
    {
        var model = CreateModel(); await model.InitializeAsync();
        model.SelectedTab!.Name = "设备查询";
        model.SelectedTab.Url = "http://127.0.0.1:9003/channel/demo?device={{deviceId}}";
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var name = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "RequestName");
            name.Focus(); Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "input-theme.png");
            Assert.True(name.IsFocused);
            var border = name.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "PART_BorderElement");
            Assert.Equal(Color.Parse("#191B21"), Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
            Assert.Equal(Color.Parse("#E4E6ED"), Assert.IsAssignableFrom<ISolidColorBrush>(name.Foreground).Color);
            Assert.Equal(new Thickness(1), border.BorderThickness);
            var url = window.GetVisualDescendants().OfType<CodeEditor>().Single(x => x.SingleLine && x.Text == model.SelectedTab.Url);
            url.Editor.TextArea.TextView.EnsureVisualLines();
            var runs = url.Editor.TextArea.TextView.VisualLines.SelectMany(x => x.Elements).ToList();
            Assert.DoesNotContain(runs, x => x.GetType().Name.Contains("Link", StringComparison.Ordinal));
            Assert.Contains(runs, x => x.TextRunProperties.ForegroundBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#FFAD7A"));
        }
        finally { await model.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task TextResponsesWrapToTheViewportWithoutChangingTheirContent()
    {
        var model = CreateModel(); await model.InitializeAsync();
        var tab = (HttpRequestTabViewModel)model.SelectedTab!;
        var window = new MainWindow { DataContext = model, Width = 1100 }; window.Show();
        try
        {
            var longText = string.Concat(Enumerable.Repeat("中文-long-response-value-0123456789", 20));
            foreach (var (type, text) in new[]
            {
                ("application/json", "{\"message\":\"" + longText + "\"}"), ("application/xml", "<message>" + longText + "</message>"),
                ("text/html", "<p>" + longText + "</p>"), ("text/plain", longText)
            })
            {
                tab.ShowHistory(new() { Body = text, ContentType = type, StatusCode = 200 });
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var response = window.GetVisualDescendants().OfType<CodeEditor>().Single(x => x.Name == "ResponseBodyEditor");
                response.Editor.TextArea.TextView.EnsureVisualLines();
                Assert.True(response.Editor.WordWrap);
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, response.Editor.HorizontalScrollBarVisibility);
                Assert.True(response.Editor.TextArea.TextView.VisualLines.Sum(x => x.TextLines.Count) > 1);
                Assert.Equal(text, response.Editor.Text);
                Assert.Equal(1, response.Editor.Document.LineCount);
                Assert.Equal(text, tab.ResponseBody);
                Assert.True(response.Editor.IsReadOnly);
            }
            SaveFrame(window, "response-wrapping.png");
        }
        finally { await model.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task LoadingIndicatorAppearsForPendingRequestAndStopsAfterCancellation()
    {
        var model = CreateModel(http: new PendingHttp()); await model.InitializeAsync();
        var tab = (HttpRequestTabViewModel)model.SelectedTab!;
        tab.Url = "http://127.0.0.1/pending";
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            var sending = tab.SendCommand.ExecuteAsync(null);
            Assert.True(tab.IsBusy); Assert.Equal("发送中…", tab.SendLabel);
            SaveFrame(window, "sending.png");
            var loading = window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(x => x.Name == "SendLoading");
            Assert.True(loading.IsVisible);
            Assert.False(loading.GetVisualAncestors().OfType<Button>().First().IsEffectivelyEnabled);
            tab.CancelCommand.Execute(null);
            await sending;
            Assert.False(tab.IsBusy); Assert.Equal("发送 ↗", tab.SendLabel);
            Assert.Contains("取消", tab.Status); Assert.False(loading.IsVisible);
        }
        finally { await model.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task UpdateChecksStayInStatusBarAndOnlyReadyVersionsPromptOnce()
    {
        var updates = new TestUpdates(); var dialogs = new TestDialogs();
        var model = CreateModel(updates, dialogs); await model.InitializeAsync();
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(dialogs.Titles);
            SaveFrame(window, "update-status.png");
            var status = window.FindControl<TextBlock>("UpdateStatusText")!;
            Assert.Equal("当前已是最新版本", status.Text);
            Assert.True(status.TranslatePoint(new Point(), window)!.Value.Y > window.Bounds.Height - 35);
            updates.Report(new("正在下载更新…", 45, "0.2.0", Busy: true)); Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "update-downloading.png");
            Assert.Empty(dialogs.Titles); Assert.Equal(45, model.UpdateProgress); Assert.False(model.IsUpdateIndeterminate);
            dialogs.Choices.Enqueue(1);
            const string notes = "- 修复参数表格输入崩溃。\n- 升级提示显示更新内容。";
            updates.Report(new("更新已下载", 100, "0.2.0", Ready: true, ReleaseNotes: notes)); Dispatcher.UIThread.RunJobs();
            Assert.Single(dialogs.Titles); Assert.True(model.UpdateOnExit);
            Assert.Contains("0.2.0", dialogs.Titles[0]);
            Assert.Contains("更新内容\n" + notes, dialogs.Messages[0]);
            SaveFrame(window, "update-ready.png");
            updates.Report(new("更新已下载", 100, "0.2.0", Ready: true, ReleaseNotes: notes)); Dispatcher.UIThread.RunJobs();
            Assert.Single(dialogs.Titles);
            updates.Report(new("当前已是最新版本")); Dispatcher.UIThread.RunJobs();
            Assert.True(model.UpdateReady);
            var restarting = false; model.ExitRequested += restart => restarting = restart;
            dialogs.Choices.Enqueue(0);
            await model.ShowUpdatePromptCommand.ExecuteAsync(null);
            Assert.Equal(2, dialogs.Titles.Count); Assert.True(restarting);
            Assert.Equal(dialogs.Messages[0], dialogs.Messages[1]);
        }
        finally { await model.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task UpdateNotesFollowTheTargetVersionAndMissingNotesStillAllowUpgrade()
    {
        var updates = new TestUpdates(); var dialogs = new TestDialogs();
        var model = CreateModel(updates, dialogs); await model.InitializeAsync();
        try
        {
            Dispatcher.UIThread.RunJobs();
            updates.Report(new("更新已下载", 100, "0.2.0", Ready: true, ReleaseNotes: "旧版本说明")); Dispatcher.UIThread.RunJobs();
            updates.Report(new("更新已下载", 100, "0.3.0", Ready: true, ReleaseNotes: "新版本说明")); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, dialogs.Messages.Count);
            Assert.Contains("新版本说明", dialogs.Messages[1]);
            Assert.DoesNotContain("旧版本说明", dialogs.Messages[1]);
            var restarting = false; model.ExitRequested += restart => restarting = restart;
            foreach (var notes in new string?[] { null, "", " \r\n " })
            {
                updates.Report(new("更新已下载", 100, "0.4.0", Ready: true, ReleaseNotes: notes)); Dispatcher.UIThread.RunJobs();
                dialogs.Choices.Enqueue(0);
                await model.ShowUpdatePromptCommand.ExecuteAsync(null);
                Assert.Contains("此版本未提供更新说明。", dialogs.Messages[^1]);
                Assert.DoesNotContain("新版本说明", dialogs.Messages[^1]);
                Assert.True(restarting);
            }
        }
        finally { await model.ShutdownAsync(); }
    }
    [AvaloniaFact]
    public async Task LongUpdateNotesCanScrollWithoutHidingUpgradeButtons()
    {
        var notes = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"- 第 {i} 项更新：修复问题并改进使用体验。"));
        var model = new DialogViewModel("发现新版本 0.2.0", "更新包已下载完成。\n\n更新内容\n" + notes, ["立即升级", "退出时升级", "稍后"]);
        var window = new DialogWindow(model); window.Show();
        try
        {
            SaveFrame(window, "update-release-notes.png");
            var message = window.FindControl<SelectableTextBlock>("DialogMessage")!;
            Assert.EndsWith(notes, message.Text);
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            scroll.ScrollToEnd(); Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
            Assert.True(window.Bounds.Height <= window.MaxHeight);
            foreach (var label in new[] { "立即升级", "退出时升级", "稍后" })
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, label));
                var position = button.TranslatePoint(new Point(), window)!.Value;
                Assert.True(button.IsEffectivelyVisible);
                Assert.True(position.Y >= scroll.TranslatePoint(new Point(), window)!.Value.Y + scroll.Bounds.Height);
                Assert.True(position.Y + button.Bounds.Height <= window.Bounds.Height);
            }
        }
        finally { window.Close(); }
    }
    private static void SaveFrame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Environment.GetEnvironmentVariable("APINET_SCREENSHOT_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(path, name));
    }
    private sealed class RecordingHttp : IHttpRequestService
    {
        public RequestDefinition? Request { get; private set; }
        public Task<HttpResult> SendAsync(RequestDefinition request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResult { StatusCode = 200, StatusText = "OK", Body = "received" });
        }
    }
    private sealed class PendingHttp : IHttpRequestService
    {
        public async Task<HttpResult> SendAsync(RequestDefinition request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); return new(); }
            catch (OperationCanceledException) { return new() { Error = "请求已取消" }; }
        }
    }
    private sealed class TestUpdates : IUpdateService
    {
        public event Action<UpdateState>? StateChanged;
        public bool IsReady { get; private set; }
        public void Report(UpdateState state) { if (state.Ready) IsReady = true; StateChanged?.Invoke(state); }
        public Task CheckAndDownloadAsync(string url, CancellationToken cancellationToken = default) { Report(new("当前已是最新版本")); return Task.CompletedTask; }
        public void Apply(bool restart) { }
    }
    private sealed class TestDialogs : IDialogService
    {
        public string? UploadPath { get; set; }
        public string? ResponsePath { get; set; }
        public List<string> Titles { get; } = [];
        public List<string> Messages { get; } = [];
        public Queue<int> Choices { get; } = new();
        public Task<int> ChooseAsync(string title, string message, string[] choices) { Titles.Add(title); Messages.Add(message); return Task.FromResult(Choices.TryDequeue(out var choice) ? choice : -1); }
        public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => Task.FromResult<ImportSelection?>(null);
        public Task<string?> PromptAsync(string title, string initial = "") => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenUploadFileAsync() => Task.FromResult(UploadPath);
        public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
        public Task<string?> SaveResponseAsync(string name) => Task.FromResult(ResponsePath);
        public Task EditEnvironmentsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
        public Task EditSettingsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
    }
}
