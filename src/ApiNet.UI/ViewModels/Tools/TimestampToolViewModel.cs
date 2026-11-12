using System.Globalization;
using ApiNet.Infrastructure.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels.Tools;

public partial class TimestampToolViewModel : ViewModelBase
{
    public static string[] Units { get; } = ["秒", "毫秒"];
    public static string[] Zones { get; } = ["本地时区", "UTC"];
    [ObservableProperty] private string _unit = Units[0];
    [ObservableProperty] private string _zone = Zones[0];
    [ObservableProperty] private string _timestamp = "";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private string _status = "无偏移的日期按所选时区解析；带 Z 或 +08:00 等偏移的日期按其偏移解析。";
    private DateTimeOffset? _lastValue;
    private bool Milliseconds => Unit == Units[1];
    private TimeZoneInfo SelectedZone => Zone == Zones[1] ? TimeZoneInfo.Utc : TimeZoneInfo.Local;

    public TimestampToolViewModel() => Show(DateTimeOffset.UtcNow);
    partial void OnUnitChanged(string? oldValue, string newValue)
    {
        if (_lastValue is { } date && Timestamp == (oldValue == Units[1] ? date.ToUnixTimeMilliseconds() : date.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture))
            Timestamp = (Milliseconds ? date.ToUnixTimeMilliseconds() : date.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);
    }
    partial void OnZoneChanged(string? oldValue, string newValue)
    {
        if (_lastValue is { } date && Date == TimeTools.FormatDate(date, oldValue == Zones[1] ? TimeZoneInfo.Utc : TimeZoneInfo.Local))
            Date = TimeTools.FormatDate(date, SelectedZone);
    }
    partial void OnTimestampChanged(string value) => Result = "";
    partial void OnDateChanged(string value) => Result = "";

    [RelayCommand] private void ToDate() => Convert(() => TimeTools.FromTimestamp(Timestamp, Milliseconds));
    [RelayCommand] private void ToTimestamp() => Convert(() => TimeTools.ParseDate(Date, SelectedZone));
    [RelayCommand] private void Now() { Show(DateTimeOffset.UtcNow); Status = "已填入当前时间"; }
    private void Convert(Func<DateTimeOffset> convert)
    {
        try { Show(convert()); Status = "转换完成"; }
        catch (Exception exception) when (exception is FormatException or ArgumentException) { Result = ""; Status = "转换失败：" + exception.Message; }
    }
    private void Show(DateTimeOffset value)
    {
        _lastValue = value;
        Timestamp = (Milliseconds ? value.ToUnixTimeMilliseconds() : value.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);
        Date = TimeTools.FormatDate(value, SelectedZone);
        Result = $"秒时间戳：{value.ToUnixTimeSeconds()}\n毫秒时间戳：{value.ToUnixTimeMilliseconds()}\n\n本地时间：{TimeTools.FormatDate(value, TimeZoneInfo.Local)}\nUTC 时间：{TimeTools.FormatDate(value, TimeZoneInfo.Utc)}";
    }
}
