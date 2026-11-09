using System.Text;
using System.Text.Json.Nodes;
using ApiNet.Core.Models;
using ApiNet.Core.Interfaces;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.Services.Documents;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Workspace;
using ApiNet.Desktop.ViewModels.Dialogs;
using ApiNet.Desktop.Views;
using ApiNet.Infrastructure.Documents;
using ApiNet.Infrastructure.Http;
using ApiNet.Infrastructure.OpenApi;
using ApiNet.Infrastructure.Persistence;
using ApiNet.Infrastructure.Updates;
using ApiNet.Infrastructure.Variables;
using ApiNet.Proxy;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ApiNet.Desktop.Tests;

public class DocumentExportWorkflowTests
{
    private static MainWindowViewModel CreateModel(ExportDialogs dialogs)
    {
        var path = Path.Combine(Path.GetTempPath(), "apinet-export-ui-" + Guid.NewGuid().ToString("N"));
        return new(new SqliteWorkspaceStore(path), new VariableResolver(), new HttpRequestService(), new OpenApiService(), new ForwardProxyService(path), new VelopackUpdateService(), dialogs);
    }
    private static IEnumerable<WorkspaceNodeViewModel> Nodes(IEnumerable<WorkspaceNodeViewModel> nodes) => nodes.SelectMany(x => new[] { x }.Concat(Nodes(x.Children)));

    [AvaloniaFact]
    public async Task MultiSelectionSurvivesSearchAndThemeAndExportsUnsavedDrafts()
    {
        var dialogs = new ExportDialogs();
        var model = CreateModel(dialogs);
        await model.InitializeAsync();
        var window = new MainWindow { DataContext = model }; window.Show();
        try
        {
            var first = model.SelectedTab!;
            first.Name = "导出中的未保存修改";
            first.Url = "{{baseUrl}}/draft";
            model.Nodes[0].IsExportSelected = true;
            var socket = Nodes(model.Nodes).Single(x => x.Id == model.Workspace.Requests.Single(x => x.Protocol == "WebSocket").Id);
            socket.IsExportSelected = true;
            model.Search = "does-not-match";
            Assert.Empty(model.Nodes);
            Assert.True(model.HasExportSelection);
            Assert.Contains("3 个请求", model.ExportSelectionSummary);
            await model.ToggleThemeCommand.ExecuteAsync(null);
            model.Search = "";
            Assert.Equal(2, Nodes(model.Nodes).Count(x => x.IsExportSelected));
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(window.GetVisualDescendants().OfType<CheckBox>().Count(x => x.IsChecked == true) >= 2);
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
            Directory.CreateDirectory(directory);
            using (var frame = window.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(Path.Combine(directory, "document-export-selection.png")); }
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Equal("api-document.md", dialogs.SavedName);
            var markdown = Encoding.UTF8.GetString(dialogs.Content!);
            Assert.Contains("未保存修改", markdown);
            Assert.Contains("{{baseUrl}}/draft", markdown);
            Assert.Contains("共 3 个请求", markdown);
            Assert.Contains("包含 3 个请求", dialogs.Scope);
            Assert.True(first.IsDirty);
            Assert.DoesNotContain(model.Workspace.Requests, x => x.Name == first.Name);
            model.ClearExportSelectionCommand.Execute(null);
            Assert.False(model.HasExportSelection);
            Assert.DoesNotContain(Nodes(model.Nodes), x => x.IsExportSelected);
        }
        finally { ThemeService.Apply("Dark"); await model.ShutdownAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task JsonExportUsesSelectedRequestAndCurrentMovedFolder()
    {
        var dialogs = new ExportDialogs { Choice = 2 };
        var model = CreateModel(dialogs); await model.InitializeAsync();
        try
        {
            var request = model.SelectedTab!;
            var folder = model.Workspace.Folders[0];
            await model.MoveNodeAsync(request.Id, null);
            model.SelectedNode = Nodes(model.Nodes).Single(x => x.Id == request.Id);
            await model.ExportCommand.ExecuteAsync(null);
            var root = JsonNode.Parse(dialogs.Content!)!;
            Assert.Single(root["paths"]!.AsObject());
            Assert.Empty(root["x-apinet-folders"]!.AsArray());
            Assert.Equal("openapi.json", dialogs.SavedName);
            Assert.Equal(folder.Id, request.Original.FolderId);
        }
        finally { await model.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task CancelEmptyFolderAndSaveFailureLeaveExportUsable()
    {
        var dialogs = new ExportDialogs { Choice = -1 };
        var model = CreateModel(dialogs); await model.InitializeAsync();
        try
        {
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Null(dialogs.Content); Assert.False(model.IsExporting);
            Assert.Contains("取消", model.Status);
            dialogs.Choice = 0; dialogs.CancelSave = true;
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Contains("取消", model.Status);
            dialogs.CancelSave = false; dialogs.FailSave = true;
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Contains("导出失败", model.Status);
            Assert.True(model.ExportCommand.CanExecute(null));
            model.Workspace.Requests.Clear();
            await model.MoveNodeAsync(model.Workspace.Folders[0].Id, null);
            model.SelectedNode = model.Nodes[0];
            var choicesBefore = dialogs.ChoiceCalls;
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Contains("没有可导出", model.Status);
            Assert.Equal(choicesBefore, dialogs.ChoiceCalls);
        }
        finally { await model.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task PdfExportProducesChineseDocumentAndPaginatesLongTableRows()
    {
        var dialogs = new ExportDialogs { Choice = 1 };
        var model = CreateModel(dialogs); await model.InitializeAsync();
        try
        {
            model.SelectedNode = Nodes(model.Nodes).Single(x => !x.IsFolder && x.Id == model.SelectedTab!.Id);
            model.SelectedTab!.Name = "中文接口 PDF 导出验证";
            await model.ExportCommand.ExecuteAsync(null);
            Assert.Equal("api-document.pdf", dialogs.SavedName);
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(dialogs.Content!, 0, 5));
            var rows = new List<string[]> { new[] { "状态", "KEY", "类型", "VALUE" }, new[] { "启用", "description", "Text", string.Join('\n', Enumerable.Range(1, 70).Select(x => $"第 {x} 行：这是一个需要跨页的长参数，中文内容应当清晰可读。")) }, new[] { "停用", "file", "File", "请自行选择文件" } };
            var document = new RequestDocument("中文接口文档与分页验证", 2,
            [new(DocumentBlockKind.Heading, "用户 / 上传文件", "folder"), new(DocumentBlockKind.Heading, "POST 上传用户资料", "request"), new(DocumentBlockKind.Paragraph, "https://example.test/" + new string('a', 200)), new(DocumentBlockKind.Table, Rows: rows), new(DocumentBlockKind.Code, "{\n  \"message\": \"你好，世界\",\n  \"host\": \"{{baseUrl}}\"\n}", "json"), new(DocumentBlockKind.Heading, "WebSocket 消息", "request"), new(DocumentBlockKind.Paragraph, "wss://example.test/ws")]);
            var pdf = PdfDocumentWriter.Write(document);
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "export-validation.pdf"), pdf);
            Assert.True(pdf.Length > 2000);
            Assert.Contains("已导出 1 个请求", model.Status);
        }
        finally { await model.ShutdownAsync(); }
    }

    private sealed class ExportDialogs : IDialogService
    {
        public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => Task.FromResult<ImportSelection?>(null);
        public int Choice { get; set; }
        public int ChoiceCalls { get; private set; }
        public bool CancelSave { get; set; }
        public bool FailSave { get; set; }
        public string? Scope { get; private set; }
        public string? SavedName { get; private set; }
        public byte[]? Content { get; private set; }
        public Task<int> ChooseAsync(string title, string message, string[] choices) { ChoiceCalls++; Scope = message; return Task.FromResult(Choice); }
        public Task<string?> WriteDocumentAsync(string name, byte[] content)
        {
            if (FailSave) throw new IOException("测试写入失败");
            if (CancelSave) return Task.FromResult<string?>(null);
            SavedName = name; Content = content; return Task.FromResult<string?>("saved/" + name);
        }
        public Task<string?> PromptAsync(string title, string initial = "") => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenUploadFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
        public Task<string?> SaveResponseAsync(string name) => Task.FromResult<string?>(null);
        public Task EditEnvironmentsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
        public Task EditSettingsAsync(WorkspaceDocument workspace) => Task.CompletedTask;
    }
}
