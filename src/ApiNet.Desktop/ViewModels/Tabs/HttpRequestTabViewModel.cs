using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Http;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiNet.Desktop.ViewModels.Tabs;

public partial class HttpRequestTabViewModel : RequestTabViewModel
{
    private readonly IHttpRequestService _http;
    private CancellationTokenSource? _request;
    private HttpResult? _lastResult;
    [ObservableProperty] private bool _canSaveResponse;
    public HttpRequestTabViewModel(RequestDefinition request, MainWindowViewModel owner, IHttpRequestService http) : base(request, owner) => _http = http;

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            var context = HistorySanitizer.SnapshotContext(Owner.Workspace);
            var resolved = Resolve(context);
            using var lifetime = new CancellationTokenSource();
            _request = lifetime;
            Status = "正在发送请求…";
            var result = await _http.SendAsync(resolved, lifetime.Token);
            ClearDownload();
            _lastResult = result;
            CanSaveResponse = result.DownloadPath is not null;
            ShowHistory(result);
            if (result.Truncated)
                Status += "  ·  响应预览截断至 4 MB";
            await Owner.RecordHistoryAsync(HistorySanitizer.Create(resolved, result, context));
            if (result.FileName is not null && CanSaveResponse)
                await SaveResponseAsync();
        }
        catch (Exception exception) { Status = exception.Message; }
        finally { _request = null; IsBusy = false; }
    }
    [RelayCommand] private void Cancel() => _request?.Cancel();
    [RelayCommand]
    private async Task SaveResponseAsync()
    {
        if (_lastResult?.DownloadPath is not { } source) return;
        var path = await Owner.Dialogs.SaveResponseAsync(_lastResult.FileName ?? "response.txt");
        if (path is null) { Status += "  ·  可点击保存响应选择目录"; return; }
        try
        {
            await using var input = File.OpenRead(source);
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            await input.CopyToAsync(output);
            Status = "响应已保存：" + path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Status = "保存失败：" + exception.Message; }
    }
    private void ClearDownload()
    {
        if (_lastResult?.DownloadPath is { } path)
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        _lastResult = null;
        CanSaveResponse = false;
    }
    public override async Task StopAsync()
    {
        _request?.Cancel();
        if (SendCommand.ExecutionTask is { IsCompleted: false } running)
            await running;
    }
    public override async ValueTask DisposeAsync() { await StopAsync(); ClearDownload(); }
}
