using ApiNet.Infrastructure.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels.Tools;

public partial class ToolsViewModel : ViewModelBase
{
    public static string[] Kinds { get; } = ["JSON 格式化", "URL 编码 / 解码", "Base64 编码 / 解码", "时间戳 / 日期转换", "Cron 解析 / 生成"];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsJson), nameof(IsCodec), nameof(IsTextTool), nameof(IsTimestamp), nameof(IsCron), nameof(Language), nameof(Hint))] private string _selectedTool = Kinds[0];
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "准备就绪";
    public bool IsJson => SelectedTool == Kinds[0];
    public bool IsCodec => SelectedTool == Kinds[1] || SelectedTool == Kinds[2];
    public bool IsTextTool => IsJson || IsCodec;
    public bool IsTimestamp => SelectedTool == Kinds[3];
    public bool IsCron => SelectedTool == Kinds[4];
    public TimestampToolViewModel Timestamp { get; } = new();
    public CronToolViewModel Cron { get; } = new();
    public string Language => IsJson ? "JSON" : "Text";
    public string Hint => IsJson ? "格式化或压缩 JSON，保留数值精度。" : SelectedTool == Kinds[1] ? "按 URL 参数编码；解码时将 + 还原为空格。" : IsTimestamp ? "Unix 时间戳与日期双向转换，支持秒、毫秒、本地时区和 UTC。" : IsCron ? "解析字段、预览执行时间，或使用模板和自定义字段生成表达式。" : "使用 UTF-8 文本进行 Base64 编码或解码。";
    partial void OnSelectedToolChanged(string value) { Output = ""; Status = "准备就绪"; }
    [RelayCommand]
    private void Transform(string action)
    {
        try
        {
            Output = IsJson ? TextTools.Json(Input, action != "minify") : SelectedTool == Kinds[1] ? action == "encode" ? TextTools.UrlEncode(Input) : TextTools.UrlDecode(Input) : action == "encode" ? TextTools.Base64Encode(Input) : TextTools.Base64Decode(Input);
            Status = $"处理完成 · {Output.Length:N0} 个字符";
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or FormatException or ArgumentException)
        { Status = "处理失败：" + exception.Message; }
    }
    [RelayCommand] private void UseOutput() { Input = Output; Status = "结果已填入输入区"; }
    [RelayCommand] private void Clear() { Input = ""; Output = ""; Status = "已清空"; }
}
