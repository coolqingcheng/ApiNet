using ApiNet.Desktop.ViewModels.Tools;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ApiNet.Desktop.Views.Tools;

public partial class CronToolView : UserControl
{
    public CronToolView() => InitializeComponent();
    private async void CopyResult(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not CronToolViewModel model) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { model.Status = "当前环境不支持剪贴板"; return; }
            var expressionOnly = sender is Button { Tag: "expression" };
            await clipboard.SetTextAsync(expressionOnly ? model.Expression : model.Expression + "\n\n" + model.Result);
            model.Status = expressionOnly ? "表达式已复制" : "结果已复制";
        }
        catch (Exception exception) { model.Status = "复制失败：" + exception.Message; }
    }
}
