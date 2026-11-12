using ApiNet.Core.Models;
using ApiNet.Infrastructure.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels.Tabs;

public abstract partial class RequestTabViewModel
{
    public static string[] ResponseFormats { get; } = ["JSON", "XML", "HTML", "JavaScript", "CSS", "Text"];
    private string _rawResponseBody = "";
    private string _rawResponseFormat = "Text";
    private long _responseRevision;
    private CancellationTokenSource? _responseCancellation;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(FormatResponseCommand), nameof(MinifyResponseCommand), nameof(RestoreResponseCommand))] private bool _isResponseProcessing;
    [ObservableProperty] private string _responseDisplayStatus = "";
    private bool CanTransformResponse() => !IsResponseProcessing && ResponseBody.Length > 0 && ResponseFormat != "Text";
    private bool CanRestoreResponse() => !IsResponseProcessing && (ResponseBody != _rawResponseBody || ResponseFormat != _rawResponseFormat);

    public void ShowHistory(HttpResult response)
    {
        _responseRevision++;
        _responseCancellation?.Cancel();
        _rawResponseBody = response.Body;
        _rawResponseFormat = ResponseTextFormatter.DetectFormat(response.ContentType, response.Body);
        ResponseBody = _rawResponseBody;
        ResponseFormat = _rawResponseFormat;
        RestoreResponseCommand.NotifyCanExecuteChanged();
        ResponseDisplayStatus = "";
        ResponseHeaders = response.Headers;
        Status = response.Error ?? $"{response.StatusCode} {response.StatusText}  ·  {response.DurationMs} ms  ·  {response.Size:N0} B";
    }

    partial void OnResponseFormatChanged(string value)
    {
        _responseRevision++;
        _responseCancellation?.Cancel();
        ResponseDisplayStatus = "";
        RestoreResponseCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTransformResponse))] private Task FormatResponseAsync() => TransformResponseAsync(true);
    [RelayCommand(CanExecute = nameof(CanTransformResponse))] private Task MinifyResponseAsync() => TransformResponseAsync(false);
    private async Task TransformResponseAsync(bool indent)
    {
        var revision = ++_responseRevision;
        var text = ResponseBody;
        var format = ResponseFormat;
        using var lifetime = new CancellationTokenSource();
        _responseCancellation = lifetime;
        IsResponseProcessing = true;
        ResponseDisplayStatus = indent ? "正在格式化…" : "正在压缩…";
        try
        {
            var result = await Task.Run(() => ResponseTextFormatter.Transform(text, format, indent, lifetime.Token), lifetime.Token);
            if (revision != _responseRevision || lifetime.IsCancellationRequested) return;
            ResponseBody = result;
            ResponseDisplayStatus = indent ? "已格式化" : "已压缩";
        }
        catch (OperationCanceledException) { if (revision == _responseRevision) ResponseDisplayStatus = "已取消处理"; }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or System.Xml.XmlException or FormatException or ArgumentException)
        { if (revision == _responseRevision) ResponseDisplayStatus = "处理失败：" + exception.Message; }
        finally { _responseCancellation = null; IsResponseProcessing = false; }
    }
    [RelayCommand] private void CancelResponseTransform() => _responseCancellation?.Cancel();
    [RelayCommand(CanExecute = nameof(CanRestoreResponse))]
    private void RestoreResponse()
    {
        ResponseBody = _rawResponseBody;
        ResponseFormat = _rawResponseFormat;
        ResponseDisplayStatus = "已恢复原文";
    }
    protected async Task StopResponseProcessingAsync()
    {
        _responseRevision++;
        _responseCancellation?.Cancel();
        if (FormatResponseCommand.ExecutionTask is { IsCompleted: false } formatting) await formatting;
        if (MinifyResponseCommand.ExecutionTask is { IsCompleted: false } minifying) await minifying;
    }
}
