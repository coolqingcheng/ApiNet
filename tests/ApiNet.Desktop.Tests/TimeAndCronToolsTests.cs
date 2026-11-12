using ApiNet.Desktop.ViewModels.Tools;
using ApiNet.Desktop.Views.Tools;
using ApiNet.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace ApiNet.Desktop.Tests;

public class TimeAndCronToolsTests
{
    [AvaloniaFact]
    public void TimestampCommandsConvertBothWaysAndPreserveUnconvertedInput()
    {
        var tool = new TimestampToolViewModel { Zone = "UTC", Timestamp = "1709164800123" };
        tool.Unit = "毫秒";
        Assert.Equal("1709164800123", tool.Timestamp);
        tool.ToDateCommand.Execute(null);
        Assert.Equal("2024-02-29 00:00:00.123 +00:00", tool.Date);
        Assert.Contains("1709164800123", tool.Result);
        tool.Date = "1970-01-01T08:00:00+08:00";
        tool.ToTimestampCommand.Execute(null);
        Assert.Equal("0", tool.Timestamp);
        tool.Timestamp = "bad"; tool.ToDateCommand.Execute(null);
        Assert.Contains("转换失败", tool.Status); Assert.Empty(tool.Result);
        tool.NowCommand.Execute(null);
        Assert.Contains("当前时间", tool.Status); Assert.NotEmpty(tool.Result);
        Assert.InRange(long.Parse(tool.Timestamp), DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [AvaloniaFact]
    public void CronTemplatesFieldsAndParserUseTheSameExpression()
    {
        var tools = new ToolsViewModel { SelectedTool = "Cron 解析 / 生成" };
        Assert.True(tools.IsCron); Assert.False(tools.IsCodec); Assert.False(tools.IsTextTool);
        var cron = tools.Cron;
        foreach (var preset in CronToolViewModel.Presets)
        {
            cron.Preset = preset; cron.ApplyPresetCommand.Execute(null);
            Assert.Contains("解析完成", cron.Status); Assert.Contains("10.", cron.Result);
        }
        Assert.True(cron.IncludeSeconds); Assert.Equal("*/30 * * * * *", cron.Expression);
        cron.Second = "15"; cron.Minute = "*/10"; cron.GenerateCommand.Execute(null);
        Assert.Equal("15 */10 * * * *", cron.Expression);
        cron.Expression = "bad cron"; cron.ParseCommand.Execute(null);
        Assert.Contains("解析失败", cron.Status); Assert.Empty(cron.Result);
        cron.Expression = "0 0 31 2 *"; cron.Format = CronToolViewModel.Formats[0]; cron.ParseCommand.Execute(null);
        Assert.Contains("没有可执行日期", cron.Result);
        tools.SelectedTool = "时间戳 / 日期转换";
        Assert.True(tools.IsTimestamp); Assert.False(tools.IsCron); Assert.False(tools.IsTextTool);
    }

    [AvaloniaFact]
    public void TimeToolsRenderAndScrollAtDesktopAndCompactWidths()
    {
        var tools = new ToolsViewModel();
        var view = new ToolsView { DataContext = tools };
        ThemeService.Apply("Dark");
        var window = new Window { Content = view, Width = 960, Height = 850, Background = ThemeService.Brush("WindowBackground") }; window.Show();
        try
        {
            tools.SelectedTool = "时间戳 / 日期转换"; tools.Timestamp.Unit = "毫秒"; tools.Timestamp.Timestamp = "1709164800123"; tools.Timestamp.ToDateCommand.Execute(null);
            SaveFrame(window, "timestamp-tool.png");
            tools.SelectedTool = "Cron 解析 / 生成"; tools.Cron.Preset = "工作日 09:00"; tools.Cron.ApplyPresetCommand.Execute(null);
            SaveFrame(window, "cron-tool.png");
            Assert.Equal("0 9 * * MON-FRI", tools.Cron.Expression);
            var cronView = view.GetLogicalDescendants().OfType<CronToolView>().Single();
            var scroll = Assert.IsType<ScrollViewer>(cronView.Content);
            scroll.ScrollToEnd(); SaveFrame(window, "cron-tool-times.png");
            Assert.True(scroll.Offset.Y > 0);
            ThemeService.Apply("Light");
            window.Background = ThemeService.Brush("WindowBackground");
            window.Width = 480; scroll.ScrollToHome(); SaveFrame(window, "cron-tool-compact.png");
            Assert.True(cronView.IsEffectivelyVisible);
        }
        finally { ThemeService.Apply("Dark"); window.Close(); }
    }

    private static void SaveFrame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Environment.GetEnvironmentVariable("APINET_SCREENSHOT_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(path, name));
    }
}
