using PostNet.Core.Models;
using PostNet.Core.Interfaces;
using PostNet.Desktop.Services;
using PostNet.Desktop.ViewModels;
using PostNet.Desktop.ViewModels.Dialogs;
using PostNet.Desktop.Views;
using PostNet.Desktop.Views.Tabs;
using PostNet.Desktop.ViewModels.Tabs;
using CommunityToolkit.Mvvm.Input;
using PostNet.Infrastructure.Http;
using PostNet.Infrastructure.OpenApi;
using PostNet.Infrastructure.Persistence;
using PostNet.Infrastructure.Updates;
using PostNet.Infrastructure.Variables;
using PostNet.Proxy;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PostNet.Desktop.Tests;

public class RequestTabsTests
{
    private static MainWindowViewModel CreateModel(TestDialogs? dialogs = null)
    {
        var path = Path.Combine(Path.GetTempPath(), "apinet-tabs-" + Guid.NewGuid().ToString("N"));
        return new(new SqliteWorkspaceStore(path), new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(path), new VelopackUpdateService(), dialogs ?? new TestDialogs());
    }
    [AvaloniaFact]
    public async Task TabHeadersStayOnOneRowAndSearchPickerActivatesAndScrollsToOffscreenRequest()
    {
        var model = CreateModel(); await model.InitializeAsync();
        for (var index = 0; index < 12; index++)
        {
            var request = new RequestDefinition { Name = $"用户接口 {index + 1} · Query User Profile", Url = $"https://example.test/users/{index + 1}", Method = index % 2 == 0 ? "GET" : "POST" };
            model.Workspace.Requests.Add(request); model.OpenRequest(request);
        }
        var last = model.SelectedTab!;
        var first = model.Tabs[0]; model.SelectedTab = first;
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            Render(window);
            var control = window.GetVisualDescendants().OfType<TabControl>().Single(x => x.Name == "RequestTabs");
            var headers = Enumerable.Range(0, model.Tabs.Count).Select(index => Assert.IsType<TabItem>(control.ContainerFromIndex(index))).ToArray();
            Assert.All(headers, header => { Assert.InRange(header.Bounds.Height, 29, 34); Assert.Equal(headers[0].Bounds.Top, header.Bounds.Top); });
            var scroll = control.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "TabHeaderScroller");
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width); Assert.InRange(scroll.Offset.X, 0, 1);
            var wheelPoint = scroll.TranslatePoint(new Point(80, 20), window)!.Value;
            window.MouseWheel(wheelPoint, new Vector(0, -1)); Render(window); Assert.True(scroll.Offset.X > 0);
            window.MouseWheel(wheelPoint, new Vector(0, 1)); Render(window); Assert.InRange(scroll.Offset.X, 0, 1);
            var button = control.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "AllTabsButton");
            Assert.True(button.Bounds.Width >= 32);
            var position = button.TranslatePoint(new Point(20, 18), window)!.Value;
            window.MouseDown(position, MouseButton.Left); window.MouseUp(position, MouseButton.Left); Render(window);
            var flyout = Assert.IsType<Flyout>(button.Flyout); Assert.True(flyout.IsOpen);
            var picker = Assert.IsType<OpenTabsView>(flyout.Content);
            var search = picker.FindControl<TextBox>("TabSearch")!;
            Assert.True(search.IsFocused);
            search.Text = "q u p 12"; Render(window);
            Assert.Same(last, Assert.Single(model.TabPicker.Matches)); Assert.Same(first, model.SelectedTab);
            Save(window, "request-tabs-search.png");
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Render(window);
            Assert.Same(last, model.SelectedTab); Assert.False(flyout.IsOpen); Assert.True(scroll.Offset.X > 0);
            Save(window, "request-tabs-dark.png");
            await model.ToggleThemeCommand.ExecuteAsync(null); Save(window, "request-tabs-light.png");
            await last.CloseCommand.ExecuteAsync(null); Render(window);
            Assert.DoesNotContain(last, model.TabPicker.Matches); Assert.Equal(12, model.TabPicker.Count);
            model.SelectedTab = first; Render(window); Assert.InRange(scroll.Offset.X, 0, 1);
            flyout.ShowAt(button); Render(window); Assert.Equal("", model.TabPicker.Search);
            var results = picker.FindControl<ListBox>("TabResults")!;
            var result = Assert.IsType<ListBoxItem>(results.ContainerFromIndex(1));
            var popup = TopLevel.GetTopLevel(result)!;
            var itemPoint = result.TranslatePoint(new Point(60, result.Bounds.Height / 2), popup)!.Value;
            popup.MouseDown(itemPoint, MouseButton.Left); popup.MouseUp(itemPoint, MouseButton.Left); Render(window);
            Assert.Same(model.Tabs[1], model.SelectedTab); Assert.False(flyout.IsOpen);
        }
        finally { ThemeService.Apply("Dark"); await model.ShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task PickerMatchesNameMethodAndUrlAndRefreshesAfterRenameAndClose()
    {
        var model = CreateModel(); await model.InitializeAsync();
        try
        {
            model.OpenRequest(model.Workspace.Requests[1]); var post = model.SelectedTab!;
            model.OpenRequest(model.Workspace.Requests[2]); var socket = model.SelectedTab!;
            model.TabPicker.Search = "post"; Assert.Same(post, Assert.Single(model.TabPicker.Matches));
            post.Name = "更新用户资料"; post.Url = "https://example.test/profile/update";
            model.TabPicker.Search = "更料"; Assert.Same(post, Assert.Single(model.TabPicker.Matches));
            model.TabPicker.Search = "prf upd"; Assert.Same(post, Assert.Single(model.TabPicker.Matches));
            model.TabPicker.Search = "WS"; Assert.Same(socket, Assert.Single(model.TabPicker.Matches));
            model.TabPicker.Search = "nothing-matches"; Assert.True(model.TabPicker.IsEmpty);
            model.TabPicker.Search = ""; Assert.Equal(model.Tabs.ToArray(), model.TabPicker.Matches);
            model.TabPicker.Activate(post); Assert.Same(post, model.SelectedTab);
            post.Name = post.Original.Name; post.Url = post.Original.Url;
            await post.CloseCommand.ExecuteAsync(null);
            Assert.DoesNotContain(post, model.TabPicker.Matches);
            model.TabPicker.Activate(post); Assert.NotSame(post, model.SelectedTab);
        }
        finally { await model.ShutdownAsync(); }
    }
    [AvaloniaFact]
    public async Task ContextMenuClosesOthersRelativeToClickedTabAndCanCloseTheLastTab()
    {
        var model = CreateModel(); await model.InitializeAsync();
        AddRequests(model, 3);
        var original = model.Tabs[0]; var anchor = model.Tabs[1];
        model.SelectedTab = original;
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            Render(window);
            var control = window.GetVisualDescendants().OfType<TabControl>().Single(x => x.Name == "RequestTabs");
            var header = Assert.IsType<TabItem>(control.ContainerFromIndex(1));
            var grid = header.GetVisualDescendants().OfType<Grid>().Single(x => x.ContextMenu is not null);
            var point = grid.TranslatePoint(new Point(55, grid.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Render(window);
            var menu = grid.ContextMenu!;
            Assert.True(menu.IsOpen); Assert.Same(original, model.SelectedTab);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(new[] { "保存", "复制为新标签页", "关闭", "关闭其他标签", "关闭左侧标签", "关闭右侧标签", "全部关闭" }, items.Select(item => item.Header));
            Assert.All(items, item => Assert.True(item.IsEffectivelyEnabled));
            Save(TopLevel.GetTopLevel(items[0])!, "request-tabs-close-menu.png");
            await ClickMenuItem(window, items.Single(item => Equals(item.Header, "关闭其他标签")));
            Assert.Same(anchor, Assert.Single(model.Tabs)); Assert.Same(anchor, model.SelectedTab);
            Assert.Equal(new[] { anchor.Id }, model.Workspace.OpenTabs.Select(tab => tab.Id));
            Assert.Equal(6, model.Workspace.Requests.Count); Assert.Single(model.TabPicker.Matches);

            Render(window);
            header = Assert.IsType<TabItem>(control.ContainerFromIndex(0));
            grid = header.GetVisualDescendants().OfType<Grid>().Single(x => x.ContextMenu is not null);
            menu = grid.ContextMenu!; menu.Open(grid); Render(window);
            items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.All(items.Where(item => item.Header?.ToString()?.StartsWith("关闭") == true && !Equals(item.Header, "关闭")), item => Assert.False(item.IsEffectivelyEnabled));
            Assert.True(items.Single(item => Equals(item.Header, "全部关闭")).IsEffectivelyEnabled);
            await ClickMenuItem(window, items.Single(item => Equals(item.Header, "全部关闭")));
            Assert.Empty(model.Tabs); Assert.Null(model.SelectedTab); Assert.True(model.IsEmpty);
            Assert.Empty(model.Workspace.OpenTabs); Assert.Empty(model.TabPicker.Matches);
        }
        finally { await model.ShutdownAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task CloseLeftAndRightPreserveAnchorAndFollowCurrentOrder()
    {
        var model = CreateModel(); await model.InitializeAsync(); AddRequests(model, 4);
        try
        {
            var tabs = model.Tabs.ToArray(); var anchor = tabs[2]; model.SelectedTab = tabs[0];
            Assert.False(tabs[0].CloseTabsToLeftCommand.CanExecute(null));
            Assert.False(tabs[^1].CloseTabsToRightCommand.CanExecute(null));
            await anchor.CloseTabsToLeftCommand.ExecuteAsync(null);
            Assert.Equal(tabs[2..], model.Tabs); Assert.Same(anchor, model.SelectedTab);
            Assert.False(anchor.CloseTabsToLeftCommand.CanExecute(null));
            Assert.True(anchor.CloseTabsToRightCommand.CanExecute(null));
            model.SelectedTab = tabs[^1];
            await anchor.CloseTabsToRightCommand.ExecuteAsync(null);
            Assert.Same(anchor, Assert.Single(model.Tabs)); Assert.Same(anchor, model.SelectedTab);
            Assert.False(anchor.CloseOtherTabsCommand.CanExecute(null));
            Assert.False(anchor.CloseTabsToRightCommand.CanExecute(null));
            Assert.Equal(new[] { anchor.Id }, model.Workspace.OpenTabs.Select(tab => tab.Id));
            AddRequests(model, 1);
            Assert.True(anchor.CloseTabsToRightCommand.CanExecute(null));
            await model.CloseOtherTabsAsync(tabs[0]); // A stale context must not close newly opened tabs.
            Assert.Equal(2, model.Tabs.Count);
        }
        finally { await model.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task BatchCloseHonorsSaveDiscardAndCancelAndRetainsLaterTabs()
    {
        var dialogs = new TestDialogs();
        var model = CreateModel(dialogs); await model.InitializeAsync(); AddRequests(model, 3);
        try
        {
            var tabs = model.Tabs.ToArray();
            for (var index = 0; index < 3; index++) tabs[index].Body = $"edited body {index}";
            dialogs.Choices.Enqueue(0); dialogs.Choices.Enqueue(1); dialogs.Choices.Enqueue(2);
            await tabs[^1].CloseAllTabsCommand.ExecuteAsync(null);
            Assert.Equal(tabs[2..], model.Tabs);
            Assert.Equal("edited body 0", model.Workspace.Requests.Single(request => request.Id == tabs[0].Id).Body);
            Assert.Equal(tabs[1].Original.Body, model.Workspace.Requests.Single(request => request.Id == tabs[1].Id).Body);
            Assert.Equal("edited body 2", tabs[2].Body); Assert.True(tabs[2].IsDirty);
            Assert.Equal(tabs[2..].Select(tab => tab.Id), model.Workspace.OpenTabs.Select(tab => tab.Id));
            Assert.Equal(3, dialogs.ChoiceCount);
            Assert.True(tabs[^1].CloseAllTabsCommand.CanExecute(null));

            // Invalid Bulk Edit prevents Save and stops the batch without discarding it.
            tabs[2].HeaderTable.ToggleBulkCommand.Execute(null);
            tabs[2].HeaderTable.BulkText = "invalid header line";
            dialogs.Choices.Enqueue(0);
            await tabs[^1].CloseAllTabsCommand.ExecuteAsync(null);
            Assert.Equal(tabs[2..], model.Tabs); Assert.True(tabs[2].IsDirty);
            Assert.Contains("批量编辑无效", tabs[2].Status);
        }
        finally { await model.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task BatchCloseConfirmsWebSocketDisconnectAndBlocksOverlappingCloseOperations()
    {
        var dialogs = new TestDialogs();
        var model = CreateModel(dialogs); await model.InitializeAsync();
        var session = new TestSocketSession();
        var socket = new WebSocketTabViewModel(model.Workspace.Requests.Single(request => request.Protocol == "WebSocket"), model, session);
        model.Tabs.Add(socket); AddRequests(model, 1);
        try
        {
            await socket.ConnectCommand.ExecuteAsync(null); Assert.True(socket.IsConnected);
            await model.Tabs[0].CloseTabsToRightCommand.ExecuteAsync(null);
            Assert.Equal(3, model.Tabs.Count); Assert.False(session.Disposed); Assert.True(socket.IsConnected);
            Assert.Equal(1, dialogs.ConfirmCount);

            var confirmation = new TaskCompletionSource<bool>();
            dialogs.Confirmation = () => confirmation.Task;
            var closing = model.Tabs[0].CloseTabsToRightCommand.ExecuteAsync(null);
            Assert.False(closing.IsCompleted);
            Assert.All(model.Tabs, tab => Assert.False(tab.CloseAllTabsCommand.CanExecute(null)));
            Assert.False(await model.CloseTabAsync(socket));
            await model.CloseAllTabsAsync(); Assert.Equal(3, model.Tabs.Count);
            confirmation.SetResult(true); await closing;
            Assert.True(session.Disposed); Assert.False(socket.IsConnected); Assert.Equal(2, dialogs.ConfirmCount);
            Assert.Single(model.Tabs); Assert.True(model.Tabs[0].CloseAllTabsCommand.CanExecute(null));
            Assert.Single(model.Workspace.OpenTabs);
        }
        finally { await model.ShutdownAsync(); }
    }

    private static void AddRequests(MainWindowViewModel model, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var request = new RequestDefinition { Name = $"请求 {index + 1}", Url = $"https://example.test/{index + 1}" };
            model.Workspace.Requests.Add(request); model.OpenRequest(request);
        }
    }
    private static async Task ClickMenuItem(Window window, MenuItem item)
    {
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command);
        var popup = TopLevel.GetTopLevel(item)!;
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), popup)!.Value;
        popup.MouseDown(point, MouseButton.Left); popup.MouseUp(point, MouseButton.Left); Render(window);
        if (command.ExecutionTask is { } running) await running;
        Render(window);
    }
    private static void Render(TopLevel window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Save(TopLevel window, string name)
    {
        Render(window); var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(directory, name));
    }
    private sealed class TestDialogs : IDialogService
    {
        public Queue<int> Choices { get; } = new();
        public int ChoiceCount { get; private set; }
        public int ConfirmCount { get; private set; }
        public Func<Task<bool>> Confirmation { get; set; } = () => Task.FromResult(false);
        public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => Task.FromResult<ImportSelection?>(null);
        public Task<string?> PromptAsync(string title, string initial = "") => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) { ConfirmCount++; return Confirmation(); }
        public Task<int> ChooseAsync(string title, string message, string[] choices) { ChoiceCount++; return Task.FromResult(Choices.TryDequeue(out var choice) ? choice : -1); }
        public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenUploadFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
        public Task<string?> SaveResponseAsync(string name) => Task.FromResult<string?>(null);
        public Task EditEnvironmentsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
        public Task EditSettingsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
    }
    private sealed class TestSocketSession : IWebSocketSession
    {
        public event Action<SocketMessage>? MessageReceived { add { } remove { } }
        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }
        public Task ConnectAsync(RequestDefinition request, CancellationToken cancellationToken) { IsConnected = true; return Task.CompletedTask; }
        public Task SendAsync(string content, bool binary, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; IsConnected = false; return ValueTask.CompletedTask; }
    }
}
