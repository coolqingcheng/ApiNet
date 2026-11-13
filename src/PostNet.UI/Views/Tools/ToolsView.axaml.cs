using Avalonia.Controls;
using Avalonia.Interactivity;
using PostNet.Desktop.ViewModels.Tools;

namespace PostNet.Desktop.Views.Tools;

public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 700;
            EditorPanels.ColumnDefinitions = new(compact ? "*" : "*,12,*");
            EditorPanels.RowDefinitions = new(compact ? "*,12,*" : "*");
            Grid.SetRow(OutputPanel, compact ? 2 : 0);
            Grid.SetColumn(OutputPanel, compact ? 0 : 2);
        };
    }
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
