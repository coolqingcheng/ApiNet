using System.Collections.ObjectModel;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Desktop.Controls;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Dialogs;
using ApiNet.Desktop.ViewModels.Tabs;
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
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ApiNet.Desktop.Tests;

public class ParameterTableTests
{
    private static MainWindowViewModel CreateModel(SqliteWorkspaceStore? store = null)
    {
        store ??= new SqliteWorkspaceStore(Path.Combine(Path.GetTempPath(), "apinet-parameters-" + Guid.NewGuid().ToString("N")));
        return new(store, new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(store.DataDirectory), new VelopackUpdateService(), new TestDialogs());
    }
    [AvaloniaFact]
    public async Task BlankRowPromotesInPlaceWithoutLosingFocusAndRendersBothThemes()
    {
        var model = CreateModel(); await model.InitializeAsync();
        var tab = model.SelectedTab!; tab.Name = "创建用户"; tab.Method = "POST"; tab.Url = "{{baseUrl}}/users";
        tab.HeaderRows.Clear();
        Add("Content-Type", "application/json", "请求正文格式");
        Add("Authorization", "Bearer {{token}}", "当前环境的访问凭证");
        Add("X-Debug", "true", "调试时启用", false);
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            var view = window.GetVisualDescendants().OfType<HttpRequestView>().Single();
            var tabs = view.GetVisualDescendants().OfType<TabControl>().First(); tabs.SelectedIndex = 1;
            Render(window);
            var table = view.GetVisualDescendants().OfType<ParameterTable>().Single(x => x.Title == "Headers");
            Assert.Equal(4, table.Model.Items.Count);
            var draft = table.Model.Items.Last();
            var key = table.GetVisualDescendants().OfType<CodeEditor>().Single(x => ReferenceEquals(x.DataContext, draft) && x.Watermark == "Key");
            key.Editor.TextArea.Focus(); window.KeyTextInput("X-Request");
            Assert.Same(draft, tab.HeaderRows.Last());
            Assert.True(key.IsKeyboardFocusWithin);
            window.KeyTextInput("-Id"); Assert.Equal("X-Request-Id", draft.Key);
            var valueCell = table.GetVisualDescendants().OfType<CodeEditor>().Single(x => ReferenceEquals(x.DataContext, draft) && x.Watermark == "Value");
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.True(valueCell.IsKeyboardFocusWithin); Assert.Equal("X-Request-Id", draft.Key);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift); window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.True(key.IsKeyboardFocusWithin);
            Assert.Null(key.Editor.TextArea.FocusAdorner);
            draft.Value = "{{requestId}}"; draft.Description = "便于关联日志与排查问题";
            Assert.Single(table.Model.Items, x => x.IsDraft);
            Assert.NotSame(draft, table.Model.Items.Last());
            Assert.Equal(4, tab.Snapshot().HeaderParameters.Count);
            Save(window, "postman-headers-dark.png");
            var focus = Assert.IsType<Border>(key.Content);
            Assert.Equal(Color.Parse("#969696"), Assert.IsAssignableFrom<ISolidColorBrush>(focus.BorderBrush).Color);
            await model.ToggleThemeCommand.ExecuteAsync(null);
            Save(window, "postman-headers-light.png");
            Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "DESCRIPTION");
            Assert.DoesNotContain(table.GetVisualDescendants().OfType<Button>(), x => x.Content is string content && content.Contains("添加参数"));
            Assert.All(table.GetVisualDescendants().OfType<Border>().Where(x => x.Classes.Contains("parameter-row")), row => Assert.InRange(row.Bounds.Height, 32, 36));
            table.Model.ToggleBulkCommand.Execute(null); Save(window, "postman-headers-bulk.png");
            table.Model.CancelBulkCommand.Execute(null);
            tab.BodyFormat = "Multipart"; tabs.SelectedIndex = 2; Render(window);
            var form = view.GetVisualDescendants().OfType<ParameterTable>().Single(x => x.AllowFiles);
            var file = form.Model.Items.Last(); file.Key = "avatar"; file.Type = "File";
            await file.BrowseFileCommand.ExecuteAsync(null); file.Description = "用户头像";
            Assert.Equal("avatar.png", Path.GetFileName(file.Value));
            Assert.Contains(file, tab.BodyRows);
            var field = form.Model.Items.Last(); field.Key = "name"; field.Value = "{{userName}}"; field.Description = "显示名称";
            Save(window, "postman-multipart-light.png");
            var keyWidths = form.GetVisualDescendants().OfType<Border>().Where(x => x.Classes.Contains("parameter-row"))
                .Select(x => Assert.IsType<Grid>(x.Child).ColumnDefinitions[1].ActualWidth).ToArray();
            Assert.All(keyWidths, width => Assert.InRange(width, keyWidths[0] - 0.5, keyWidths[0] + 0.5));
        }
        finally { ThemeService.Apply("Dark"); await model.ShutdownAsync(); window.Close(); }

        void Add(string key, string value, string description, bool enabled = true)
        {
            tab.AddHeaderCommand.Execute(null);
            var row = tab.HeaderRows.Last(); row.Key = key; row.Value = value; row.Description = description; row.Enabled = enabled;
        }
    }

    [AvaloniaFact]
    public void BulkEditPreservesDuplicateDescriptionsFileTypesAndDisabledValuesAndRejectsInvalidInput()
    {
        var rows = new ObservableCollection<ParameterRowViewModel>();
        foreach (var parameter in new RequestParameter[] { new() { Key = "tag", Value = "one", Description = "第一项" }, new() { Key = "tag", Value = "two", Description = "第二项", Enabled = false }, new() { Key = "file", Value = "avatar.png", Type = "File", Description = "文件字段" } })
            rows.Add(new(parameter, row => rows.Remove(row)));
        using var table = new ParameterTableViewModel(() => Task.FromResult<string?>(null)); table.SetRows(rows);
        table.ToggleBulkCommand.Execute(null);
        Assert.Contains("// tag: two", table.BulkText);
        table.BulkText = "tag: https://example.test:8443\n// tag: new\nfile: new.png\nother: {{host}}";
        table.ToggleBulkCommand.Execute(null);
        Assert.False(table.IsBulkEditing); Assert.Equal(4, rows.Count);
        Assert.Equal("https://example.test:8443", rows[0].Value); Assert.Equal("第一项", rows[0].Description);
        Assert.Equal("第二项", rows[1].Description); Assert.False(rows[1].Enabled);
        Assert.True(rows[2].IsFile); Assert.Equal("文件字段", rows[2].Description);
        table.ToggleBulkCommand.Execute(null); table.BulkText = "valid: value\ninvalid line"; table.ToggleBulkCommand.Execute(null);
        Assert.True(table.IsBulkEditing); Assert.Contains("第 2 行", table.BulkError);
        Assert.Equal(4, rows.Count); Assert.Equal("https://example.test:8443", rows[0].Value);
        table.CancelBulkCommand.Execute(null); Assert.False(table.IsBulkEditing);
        table.AllEnabled = false; Assert.All(rows, row => Assert.False(row.Enabled));
        table.AllEnabled = true; Assert.All(rows, row => Assert.True(row.Enabled));
        rows[0].Enabled = false; Assert.Null(table.AllEnabled);
        rows[0].RemoveCommand.Execute(null); Assert.Equal(3, rows.Count);
        Assert.Single(table.Items, row => row.IsDraft);
    }

    [AvaloniaFact]
    public async Task DescriptionsPersistAndSurviveUrlEditsWhileEmptyDraftDoesNotDirtyTheRequest()
    {
        var store = new SqliteWorkspaceStore(Path.Combine(Path.GetTempPath(), "apinet-description-" + Guid.NewGuid().ToString("N")));
        var model = CreateModel(store); await model.InitializeAsync();
        var tab = model.SelectedTab!;
        using var table = new ParameterTableViewModel(() => Task.FromResult<string?>(null)); table.SetRows(tab.HeaderRows);
        try
        {
            Assert.False(tab.IsDirty); Assert.Empty(tab.Snapshot().HeaderParameters);
            var draft = table.Items.Last(); draft.Key = "X-Client"; draft.Value = "ApiNet";
            await tab.SaveCommand.ExecuteAsync(null); Assert.False(tab.IsDirty);
            tab.HeaderRows[0].Description = "客户端标识"; Assert.True(tab.IsDirty);
            tab.Url = "https://example.test/?tag=a&tag=b";
            tab.QueryRows[0].Description = "第一项"; tab.QueryRows[1].Description = "第二项";
            tab.Url = "https://example.test/?tag=changed&tag=b&page=2";
            Assert.Equal("第一项", tab.QueryRows[0].Description); Assert.Equal("第二项", tab.QueryRows[1].Description);
            await tab.SaveCommand.ExecuteAsync(null); Assert.False(tab.IsDirty);
            var loaded = (await store.LoadAsync())!.Requests.Single(x => x.Id == tab.Id);
            Assert.Equal("客户端标识", loaded.HeaderParameters.Single().Description);
            Assert.Equal("第一项", loaded.QueryParameters[0].Description);
            Assert.Single(loaded.HeaderParameters);
        }
        finally { await model.ShutdownAsync(); }
    }

    private static void Render(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    [AvaloniaFact]
    public async Task PendingBulkEditsAreUsedBySendSaveAndDraftPersistenceWithoutLeavingBulkMode()
    {
        var store = new SqliteWorkspaceStore(Path.Combine(Path.GetTempPath(), "apinet-bulk-" + Guid.NewGuid().ToString("N")));
        var model = CreateModel(store); await model.InitializeAsync();
        var tab = model.SelectedTab!; tab.Url = "https://example.test/users";
        try
        {
            tab.HeaderTable.ToggleBulkCommand.Execute(null);
            tab.HeaderTable.BulkText = "X-Client: ApiNet\n// X-Debug: true";
            tab.QueryTable.ToggleBulkCommand.Execute(null); tab.QueryTable.BulkText = "page: 2\ntag: one\ntag: two";
            Assert.True(tab.IsDirty);
            var resolved = tab.Resolve();
            Assert.Equal("https://example.test/users?page=2&tag=one&tag=two", resolved.Url);
            Assert.Contains("X-Client: ApiNet", resolved.Headers); Assert.DoesNotContain("X-Debug", resolved.Headers);
            Assert.True(tab.HeaderTable.IsBulkEditing); Assert.False(tab.HeaderTable.HasPendingBulkChanges);
            tab.HeaderTable.BulkText = "X-Client: Saved";
            await tab.SaveCommand.ExecuteAsync(null);
            Assert.False(tab.IsDirty); Assert.True(tab.HeaderTable.IsBulkEditing);
            Assert.Equal("Saved", (await store.LoadAsync())!.Requests.Single(x => x.Id == tab.Id).HeaderParameters.Single().Value);

            tab.HeaderTable.BulkText = "X-Client: Draft";
            await model.SaveDraftsAsync();
            var draft = (await store.LoadAsync())!.OpenTabs.Single(x => x.Id == tab.Id);
            Assert.Equal("Draft", draft.HeaderParameters.Single().Value);
            Assert.True(tab.IsDirty); Assert.True(tab.HeaderTable.IsBulkEditing);

            tab.HeaderTable.BulkText = "X-Client: Wrong\ninvalid line";
            await tab.SaveCommand.ExecuteAsync(null);
            Assert.Contains("第 2 行", model.Status);
            Assert.Throws<InvalidOperationException>(() => tab.Resolve());
            Assert.Equal("Draft", tab.HeaderRows.Single().Value);
            Assert.Equal("Saved", (await store.LoadAsync())!.Requests.Single(x => x.Id == tab.Id).HeaderParameters.Single().Value);
            tab.HeaderTable.CancelBulkCommand.Execute(null);
            Assert.Equal("Draft", tab.HeaderRows.Single().Value);
        }
        finally { await model.ShutdownAsync(); }
    }

    private static void Save(Window window, string name)
    {
        Render(window);
        var directory = Environment.GetEnvironmentVariable("APINET_SCREENSHOT_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(directory, name));
    }
    private sealed class TestDialogs : IDialogService
    {
        public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => Task.FromResult<ImportSelection?>(null);
        public Task<string?> PromptAsync(string title, string initial = "") => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<int> ChooseAsync(string title, string message, string[] choices) => Task.FromResult(-1);
        public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenUploadFileAsync() => Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), "avatar.png"));
        public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
        public Task<string?> SaveResponseAsync(string name) => Task.FromResult<string?>(null);
        public Task EditEnvironmentsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
        public Task EditSettingsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
    }
}
