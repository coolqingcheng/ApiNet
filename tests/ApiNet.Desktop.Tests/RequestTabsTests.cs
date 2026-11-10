using ApiNet.Core.Models;
using ApiNet.Core.Interfaces;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Dialogs;
using ApiNet.Desktop.Views;
using ApiNet.Desktop.Views.Tabs;
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
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ApiNet.Desktop.Tests;

public class RequestTabsTests
{
    private static MainWindowViewModel CreateModel()
    {
        var path = Path.Combine(Path.GetTempPath(), "apinet-tabs-" + Guid.NewGuid().ToString("N"));
        return new(new SqliteWorkspaceStore(path), new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(path), new VelopackUpdateService(), new TestDialogs());
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
            Assert.All(headers, header => { Assert.InRange(header.Bounds.Height, 37, 46); Assert.Equal(headers[0].Bounds.Top, header.Bounds.Top); });
            var scroll = control.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "TabHeaderScroller");
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width); Assert.InRange(scroll.Offset.X, 0, 1);
            var wheelPoint = scroll.TranslatePoint(new Point(80, 20), window)!.Value;
            window.MouseWheel(wheelPoint, new Vector(0, -1)); Render(window); Assert.True(scroll.Offset.X > 0);
            window.MouseWheel(wheelPoint, new Vector(0, 1)); Render(window); Assert.InRange(scroll.Offset.X, 0, 1);
            var button = control.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "AllTabsButton");
            Assert.True(button.Bounds.Width >= 40);
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
    private static void Render(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Save(Window window, string name)
    {
        Render(window); var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(directory, name));
    }
    private sealed class TestDialogs : IDialogService
    {
        public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => Task.FromResult<ImportSelection?>(null);
        public Task<string?> PromptAsync(string title, string initial = "") => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<int> ChooseAsync(string title, string message, string[] choices) => Task.FromResult(-1);
        public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenUploadFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
        public Task<string?> SaveResponseAsync(string name) => Task.FromResult<string?>(null);
        public Task EditEnvironmentsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
        public Task EditSettingsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
    }
}
