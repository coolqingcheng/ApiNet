using PostNet.Infrastructure.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels.Tools;

public partial class CronToolViewModel : ViewModelBase
{
    public static string[] Formats { get; } = ["5 段：分 时 日 月 周", "6 段：秒 分 时 日 月 周"];
    public static string[] Presets { get; } = ["每分钟", "每 5 分钟", "每小时", "每天 09:00", "工作日 09:00", "每周一 09:00", "每月 1 日 00:00", "每 30 秒"];
    public static string[] Zones => TimestampToolViewModel.Zones;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IncludeSeconds))] private string _format = Formats[0];
    [ObservableProperty] private string _zone = Zones[0];
    [ObservableProperty] private string _expression = "*/5 * * * *";
    [ObservableProperty] private string _preset = Presets[1];
    [ObservableProperty] private string _second = "0";
    [ObservableProperty] private string _minute = "*/5";
    [ObservableProperty] private string _hour = "*";
    [ObservableProperty] private string _day = "*";
    [ObservableProperty] private string _month = "*";
    [ObservableProperty] private string _week = "*";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private string _status = "填写表达式解析，或从下方字段生成。所有时间只用于预览。";
    public bool IncludeSeconds => Format == Formats[1];
    private TimeZoneInfo SelectedZone => Zone == Zones[1] ? TimeZoneInfo.Utc : TimeZoneInfo.Local;

    partial void OnExpressionChanged(string value) => Result = "";
    partial void OnFormatChanged(string value) => Result = "";
    partial void OnZoneChanged(string value) => Result = "";
    [RelayCommand] private void Parse() => Run(() => Expression);
    [RelayCommand] private void Generate() => Run(() => CronTools.Generate(IncludeSeconds, Second, Minute, Hour, Day, Month, Week));
    [RelayCommand]
    private void ApplyPreset()
    {
        var fields = Preset switch
        {
            "每分钟" => "* * * * *", "每 5 分钟" => "*/5 * * * *", "每小时" => "0 * * * *",
            "每天 09:00" => "0 9 * * *", "工作日 09:00" => "0 9 * * MON-FRI", "每周一 09:00" => "0 9 * * MON",
            "每月 1 日 00:00" => "0 0 1 * *", "每 30 秒" => "*/30 * * * * *", _ => Expression
        };
        Format = fields.Split(' ').Length == 6 ? Formats[1] : Formats[0];
        Run(() => fields);
    }
    private void Run(Func<string> create)
    {
        try
        {
            var from = DateTimeOffset.UtcNow;
            var parsed = CronTools.Parse(create(), IncludeSeconds, from, SelectedZone);
            Expression = parsed.Expression;
            var values = parsed.Fields.Select(x => x.Value).ToArray();
            var offset = IncludeSeconds ? 1 : 0;
            Second = IncludeSeconds ? values[0] : "0";
            Minute = values[offset]; Hour = values[offset + 1]; Day = values[offset + 2]; Month = values[offset + 3]; Week = values[offset + 4];
            var fields = string.Join('\n', parsed.Fields.Select(x => $"{x.Name}：{x.Value}   ·   {x.Meaning}"));
            var times = parsed.Occurrences.Count == 0 ? "此表达式没有可执行日期。" : string.Join('\n', parsed.Occurrences.Select((x, i) => $"{i + 1,2}.  {TimeTools.FormatDate(x, SelectedZone)}"));
            Result = fields + $"\n\n从 {TimeTools.FormatDate(from, SelectedZone)} 之后开始\n接下来 10 次执行时间（{Zone}）\n" + times;
            Status = parsed.Occurrences.Count == 0 ? "表达式有效，但没有可执行日期" : "解析完成 · 共预览 10 次执行时间";
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException) { Result = ""; Status = "解析失败：" + exception.Message; }
    }
}
