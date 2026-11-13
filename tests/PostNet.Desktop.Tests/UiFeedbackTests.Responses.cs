using System.Text;
using PostNet.Core.Interfaces;
using PostNet.Core.Models;
using PostNet.Desktop.Controls;
using PostNet.Desktop.ViewModels.Tabs;
using PostNet.Desktop.Views;
using PostNet.Infrastructure.Persistence;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PostNet.Desktop.Tests;

public partial class UiFeedbackTests
{
    [AvaloniaFact]
    public async Task ResponseToolbarFormatsCompressesAndRestoresWithoutChangingDownloadOrHistory()
    {
        const string raw = "{\"message\":\"中文 a  b\",\"items\":[1,2,3],\"id\":90071992547409931234}";
        var directory = Path.Combine(Path.GetTempPath(), "apinet-response-format-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "original.json"); await File.WriteAllTextAsync(source, raw, new UTF8Encoding(false));
        var dialogs = new TestDialogs { ResponsePath = Path.Combine(directory, "saved.json") };
        var result = new HttpResult { Body = raw, ContentType = "application/json", StatusCode = 200, StatusText = "OK", Size = Encoding.UTF8.GetByteCount(raw), DownloadPath = source };
        var store = new SqliteWorkspaceStore(Path.Combine(directory, "workspace"));
        var model = CreateModel(dialogs: dialogs, http: new ResponseHttp(result), store: store); await model.InitializeAsync();
        var tab = (HttpRequestTabViewModel)model.SelectedTab!;
        var window = new MainWindow { DataContext = model, Width = 1100 }; window.Show();
        try
        {
            await tab.SendCommand.ExecuteAsync(null);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var request = tab.Body; var dirty = tab.IsDirty; var networkStatus = tab.Status;
            var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "FormatResponseButton");
            Assert.Same(tab.FormatResponseCommand, button.Command); Assert.True(button.IsEffectivelyEnabled);
            await tab.FormatResponseCommand.ExecuteAsync(null);
            Assert.Contains('\n', tab.ResponseBody); Assert.Equal("已格式化", tab.ResponseDisplayStatus);
            SaveFrame(window, "response-formatted.png");
            Assert.True(window.GetVisualDescendants().OfType<CodeEditor>().Single(x => x.Name == "ResponseBodyEditor").Editor.WordWrap);
            await tab.MinifyResponseCommand.ExecuteAsync(null); Assert.Equal(raw, tab.ResponseBody);
            await tab.FormatResponseCommand.ExecuteAsync(null);
            await tab.SaveResponseCommand.ExecuteAsync(null);
            Assert.Equal(raw, await File.ReadAllTextAsync(dialogs.ResponsePath!));
            Assert.Equal(raw, result.Body); Assert.Equal(raw, (await store.GetHistoryDetailAsync(model.History.First().Id))!.Response.Body);
            Assert.Equal(request, tab.Body); Assert.Equal(dirty, tab.IsDirty);
            tab.RestoreResponseCommand.Execute(null); Assert.Equal(raw, tab.ResponseBody); Assert.False(tab.RestoreResponseCommand.CanExecute(null));
            tab.ShowHistory(new() { Body = "<root><id>1</id></root>", ContentType = "application/xml", StatusCode = 201 });
            Assert.Equal("XML", tab.ResponseFormat); Assert.Empty(tab.ResponseDisplayStatus);
            await tab.FormatResponseCommand.ExecuteAsync(null); Assert.Contains('\n', tab.ResponseBody);
            Assert.NotEqual(networkStatus, tab.Status); Assert.StartsWith("201", tab.Status);
        }
        finally { await model.ShutdownAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task InvalidResponseFormattingPreservesTextAndAllowsManualTypeSelection()
    {
        var model = CreateModel(); await model.InitializeAsync();
        var tab = model.SelectedTab!;
        try
        {
            tab.ShowHistory(new() { Body = "{\"incomplete\":", ContentType = "application/json", StatusCode = 200 });
            var raw = tab.ResponseBody;
            await tab.FormatResponseCommand.ExecuteAsync(null);
            Assert.Equal(raw, tab.ResponseBody); Assert.Contains("处理失败", tab.ResponseDisplayStatus);
            tab.ShowHistory(new() { Body = "ordinary text", ContentType = "text/plain", StatusCode = 200 });
            Assert.False(tab.FormatResponseCommand.CanExecute(null)); Assert.False(tab.MinifyResponseCommand.CanExecute(null));
            tab.ShowHistory(new() { Body = "{\"ok\":true}", ContentType = "text/html", StatusCode = 200 });
            Assert.Equal("HTML", tab.ResponseFormat);
            tab.ResponseFormat = "JSON"; await tab.FormatResponseCommand.ExecuteAsync(null);
            Assert.Contains('\n', tab.ResponseBody); Assert.Equal("已格式化", tab.ResponseDisplayStatus);
            tab.RestoreResponseCommand.Execute(null); Assert.Equal("HTML", tab.ResponseFormat);
        }
        finally { await model.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task NewResponsesAndCancellationPreventOldFormattingFromReplacingTheDisplay()
    {
        var model = CreateModel(); await model.InitializeAsync();
        var tab = model.SelectedTab!;
        var large = "[" + string.Join(',', Enumerable.Repeat("{\"value\":\"long response\"}", 200000)) + "]";
        try
        {
            tab.ShowHistory(new() { Body = large, ContentType = "application/json" });
            var processing = tab.FormatResponseCommand.ExecuteAsync(null);
            Assert.True(tab.IsResponseProcessing); Assert.False(processing.IsCompleted);
            tab.CancelResponseTransformCommand.Execute(null); await processing;
            Assert.Equal(large, tab.ResponseBody); Assert.Equal("已取消处理", tab.ResponseDisplayStatus);
            processing = tab.FormatResponseCommand.ExecuteAsync(null);
            tab.ShowHistory(new() { Body = "{\"new\":true}", ContentType = "application/json" });
            await processing;
            Assert.Equal("{\"new\":true}", tab.ResponseBody); Assert.Empty(tab.ResponseDisplayStatus);
        }
        finally { await model.ShutdownAsync(); }
    }

    private sealed class ResponseHttp(HttpResult result) : IHttpRequestService
    {
        public Task<HttpResult> SendAsync(RequestDefinition request, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
