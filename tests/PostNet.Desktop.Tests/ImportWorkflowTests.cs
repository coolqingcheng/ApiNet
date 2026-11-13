using PostNet.Core.Interfaces;
using PostNet.Core.Models;
using PostNet.Desktop.Services;
using PostNet.Desktop.ViewModels;
using PostNet.Desktop.ViewModels.Dialogs;
using PostNet.Desktop.Views.Dialogs;
using PostNet.Infrastructure.Http;
using PostNet.Infrastructure.Imports;
using PostNet.Infrastructure.OpenApi;
using PostNet.Infrastructure.Persistence;
using PostNet.Infrastructure.Updates;
using PostNet.Infrastructure.Variables;
using PostNet.Proxy;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace PostNet.Desktop.Tests;

public class ImportWorkflowTests
{
    [AvaloniaFact]
    public void ImportPickerRendersSoftwareChoicesAndSupportsLocalFileAndCancelActions()
    {
        var sources = new[] { new LocalImportSource("api", "ApiPost", "ApiPost", Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "Example", "apipost"), "Apipost.exe"), new LocalImportSource("pm", "Postman", "Postman", Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "Example", "Postman"), BackupPath: "backup.json") };
        var model = new ImportPickerViewModel(sources);
        var window = new ImportPickerWindow(model);
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("APINET_SCREENSHOT_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, "import-picker.png"));
        window.Close();
        ImportSelection? result = null;
        model.Completed += selection => result = selection;
        model.SelectedSource = sources[1]; model.ImportLocalCommand.Execute(null);
        Assert.Same(sources[1], result!.Source);
        model.ChooseFileCommand.Execute(null); Assert.NotNull(result); Assert.Null(result.Source);
        model.ChooseUrlCommand.Execute(null); Assert.True(result!.Online); Assert.Null(result.Source);
        model.CancelCommand.Execute(null); Assert.Null(result);
        var empty = new ImportPickerViewModel([]);
        Assert.False(empty.ImportLocalCommand.CanExecute(null));
    }
    [AvaloniaFact]
    public async Task OnlineSelectionBindsUrlToChosenFolderAndRefreshesIncrementally()
    {
        var path = Path.Combine(Path.GetTempPath(), "apinet-online-ui-" + Guid.NewGuid().ToString("N"));
        var imports = new TestImports(); var dialogs = new TestDialogs { Online = true };
        var model = new MainWindowViewModel(new SqliteWorkspaceStore(path), new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(path), new VelopackUpdateService(), dialogs, imports);
        await model.InitializeAsync();
        try
        {
            model.SelectedNode = model.Nodes.First(x => x.IsFolder); var folder = model.SelectedNode.Id;
            await model.ImportCommand.ExecuteAsync(null);
            Assert.Equal(1, imports.UrlReads); Assert.Equal(0, dialogs.FilePickers);
            var source = Assert.Single(model.Workspace.ImportSources); Assert.Equal("https://example.test/v3/api-docs", source.Location); Assert.Equal(folder, source.FolderId);
            var imported = model.Workspace.Requests.Single(x => x.SourceId == source.Id); imported.Name = "本地请求名";
            await model.RefreshImportCommand.ExecuteAsync(null);
            Assert.Equal(2, imports.UrlReads); Assert.Single(model.Workspace.Requests, x => x.SourceId == source.Id); Assert.Equal("本地请求名", imported.Name);
            Assert.Contains("/v2", imported.Url); Assert.Contains(dialogs.Prompts, x => x.Contains("在线"));
        }
        finally { await model.ShutdownAsync(); }
    }
    [AvaloniaFact]
    public async Task LocalSelectionImportsIntoSelectedFolderAndRefreshesWithoutDuplicateRequests()
    {
        var path = Path.Combine(Path.GetTempPath(), "apinet-import-ui-" + Guid.NewGuid().ToString("N"));
        var store = new SqliteWorkspaceStore(path);
        var imports = new TestImports(); var dialogs = new TestDialogs();
        var model = new MainWindowViewModel(store, new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(path), new VelopackUpdateService(), dialogs, imports);
        await model.InitializeAsync();
        try
        {
            model.SelectedNode = model.Nodes.First(x => x.IsFolder);
            var target = model.SelectedNode.Id;
            await model.ImportCommand.ExecuteAsync(null);
            Assert.Equal(1, imports.Reads);
            Assert.Equal(0, dialogs.FilePickers);
            Assert.Equal(target, Assert.Single(model.Workspace.ImportSources).FolderId);
            var request = model.Workspace.Requests.Single(x => x.SourceId is not null);
            Assert.Equal("{{host}}/echo", request.Url);
            model.OpenRequest(request);
            Assert.True(model.SelectedTab!.IsFormBody);
            Assert.Equal("{{term}}", Assert.Single(model.SelectedTab.BodyRows).Value);
            model.SelectedTab.BodyRows[0].Value = "本地修改";
            Assert.True(model.SelectedTab.IsDirty);
            await model.SelectedTab.SaveCommand.ExecuteAsync(null);
            Assert.False(model.SelectedTab.IsDirty);
            await model.RefreshImportCommand.ExecuteAsync(null);
            Assert.Equal(2, imports.Reads);
            Assert.Single(model.Workspace.Requests, x => x.SourceId is not null);
            Assert.Equal("本地修改", model.SelectedTab.BodyRows[0].Value);
            Assert.Contains(model.Environments, x => x.Name == "开发");
        }
        finally { await model.ShutdownAsync(); }
    }
    private sealed class TestImports : IWorkspaceImportService
    {
        private readonly WorkspaceImportService _real = new(new OpenApiService());
        public int Reads { get; private set; }
        public int UrlReads { get; private set; }
        private readonly LocalImportSource _source = new("test", "Postman", "Postman", Path.Combine(Path.GetTempPath(), "synthetic-postman"));
        public Task<IReadOnlyList<LocalImportSource>> DetectLocalAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalImportSource>>([_source]);
        public Task<string> ReadUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            UrlReads++;
            return Task.FromResult("{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"Test\",\"version\":\"1\"},\"paths\":{\"/v" + UrlReads + "\":{\"get\":{\"operationId\":\"same\",\"summary\":\"Remote " + UrlReads + "\",\"responses\":{\"200\":{\"description\":\"OK\"}}}}}}");
        }
        public Task<string> ReadLocalAsync(LocalImportSource source, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult("""{"collections":[{"info":{"name":"集合"},"item":[{"id":"r","name":"表单","request":{"method":"POST","url":"{{host}}/echo","body":{"mode":"urlencoded","urlencoded":[{"key":"text","value":"{{term}}"}]}}}]}],"environments":[{"id":"e","name":"开发","values":[{"key":"host","value":"https://example.test"}]}]}""");
        }
        public ImportPlan Preview(WorkspaceDocument workspace, string json, string? folderId, string location) => _real.Preview(workspace, json, folderId, location);
        public void Apply(WorkspaceDocument workspace, ImportPlan plan, bool overwriteConflicts) => _real.Apply(workspace, plan, overwriteConflicts);
    }
    private sealed class TestDialogs : IDialogService
    {
        public bool Online { get; set; }
        public List<string> Prompts { get; } = [];
        public int FilePickers { get; private set; }
        public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => Task.FromResult<ImportSelection?>(Online ? new(null, true) : new(sources[0]));
        public Task<string?> OpenJsonAsync() { FilePickers++; return Task.FromResult<string?>(null); }
        public Task<string?> PromptAsync(string title, string initial = "") { Prompts.Add(title); return Task.FromResult<string?>(Online ? "https://example.test/v3/api-docs" : null); }
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<int> ChooseAsync(string title, string message, string[] choices) => Task.FromResult(0);
        public Task<string?> OpenUploadFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
        public Task<string?> SaveResponseAsync(string name) => Task.FromResult<string?>(null);
        public Task EditEnvironmentsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
        public Task EditSettingsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
    }
}
