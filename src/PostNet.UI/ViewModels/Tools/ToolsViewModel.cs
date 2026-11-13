using PostNet.Infrastructure.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels.Tools;

public partial class ToolsViewModel : ViewModelBase
{
    public static string[] Kinds { get; } = ["JSON 格式化", "URL 编码 / 解码", "Base64 编码 / 解码", "时间戳 / 日期转换", "Cron 解析 / 生成"];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsJson), nameof(IsCodec), nameof(IsTextTool), nameof(IsTimestamp), nameof(IsCron), nameof(Language), nameof(Hint))] private string _selectedTool = Kinds[0];
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "准备就绪";
    [ObservableProperty] private bool _isBusy;
    private CancellationTokenSource? _transformCancellation;
    private long _revision;
    public bool IsJson => SelectedTool == Kinds[0];
    public bool IsCodec => SelectedTool == Kinds[1] || SelectedTool == Kinds[2];
    public bool IsTextTool => IsJson || IsCodec;
    public bool IsTimestamp => SelectedTool == Kinds[3];
    public bool IsCron => SelectedTool == Kinds[4];
    public TimestampToolViewModel Timestamp { get; } = new();
    public CronToolViewModel Cron { get; } = new();
    public string Language => IsJson ? "JSON" : "Text";
    public string Hint => IsJson ? "格式化或压缩 JSON，保留数值精度。" : SelectedTool == Kinds[1] ? "按 URL 参数编码；解码时将 + 还原为空格。" : IsTimestamp ? "Unix 时间戳与日期双向转换，支持秒、毫秒、本地时区和 UTC。" : IsCron ? "解析字段、预览执行时间，或使用模板和自定义字段生成表达式。" : "使用 UTF-8 文本进行 Base64 编码或解码。";
    partial void OnSelectedToolChanged(string value) { InvalidateTransform(); Output = ""; Status = "准备就绪"; }
    partial void OnInputChanged(string value)
    {
        InvalidateTransform();
        if (IsBusy) Status = "输入已修改，已取消旧内容的处理";
    }
    private void InvalidateTransform() { _revision++; _transformCancellation?.Cancel(); }
    [RelayCommand]
    private async Task TransformAsync(string action)
    {
        var revision = ++_revision;
        var input = Input;
        var tool = SelectedTool;
        using var lifetime = new CancellationTokenSource();
        _transformCancellation = lifetime;
        IsBusy = true;
        Status = "正在处理…";
        try
        {
            var result = await Task.Run(() => tool == Kinds[0] ? TextTools.Json(input, action != "minify", lifetime.Token) : tool == Kinds[1] ? action == "encode" ? TextTools.UrlEncode(input) : TextTools.UrlDecode(input) : action == "encode" ? TextTools.Base64Encode(input) : TextTools.Base64Decode(input), lifetime.Token);
            if (revision != _revision || lifetime.IsCancellationRequested) return;
            Output = result;
            Status = $"处理完成 · {Output.Length:N0} 个字符";
        }
        catch (OperationCanceledException) { if (revision == _revision) Status = "已取消处理"; }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or FormatException or ArgumentException)
        { if (revision == _revision) Status = "处理失败：" + exception.Message; }
        finally { _transformCancellation = null; IsBusy = false; }
    }
    [RelayCommand] private void CancelTransform() { _transformCancellation?.Cancel(); }
    [RelayCommand] private void UseOutput() { InvalidateTransform(); Input = Output; Status = "结果已填入输入区"; }
    [RelayCommand] private void Clear() { InvalidateTransform(); Input = ""; Output = ""; Status = "已清空"; }
    public async Task StopAsync()
    {
        InvalidateTransform();
        if (TransformCommand.ExecutionTask is { IsCompleted: false } processing) await processing;
    }
}
