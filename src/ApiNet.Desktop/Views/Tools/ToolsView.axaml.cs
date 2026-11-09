using Avalonia.Controls;
using Avalonia.Interactivity;
using ApiNet.Desktop.ViewModels.Tools;

namespace ApiNet.Desktop.Views.Tools;

public partial class ToolsView : UserControl
{
    public ToolsView() => InitializeComponent();
    private async void CopyOutput(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ToolsViewModel model) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { model.Status = "当前环境不支持剪贴板"; return; }
            await clipboard.SetTextAsync(model.Output);
            model.Status = "结果已复制";
        }
        catch (Exception exception) { model.Status = "复制失败：" + exception.Message; }
    }
}
