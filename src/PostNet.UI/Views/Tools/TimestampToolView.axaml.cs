using PostNet.Desktop.ViewModels.Tools;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PostNet.Desktop.Views.Tools;

public partial class TimestampToolView : UserControl
{
    public TimestampToolView() => InitializeComponent();
    private async void CopyResult(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not TimestampToolViewModel model) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { model.Status = "当前环境不支持剪贴板"; return; }
            await clipboard.SetTextAsync(model.Result); model.Status = "结果已复制";
        }
        catch (Exception exception) { model.Status = "复制失败：" + exception.Message; }
    }
}
