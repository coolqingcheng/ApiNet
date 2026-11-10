using ApiNet.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ApiNet.Desktop.Views.Tabs;

public partial class RequestTabsView : UserControl
{
    public RequestTabsView() => InitializeComponent();
    private void OnHeaderWheel(object? sender, PointerWheelEventArgs args)
    {
        if (sender is not ScrollViewer scroll || scroll.Extent.Width <= scroll.Viewport.Width) return;
        scroll.Offset = new Vector(scroll.Offset.X - (args.Delta.X != 0 ? args.Delta.X : args.Delta.Y) * 64, 0);
        args.Handled = true;
    }
    private void OnSelectedTabChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, RequestTabs)) return;
        Dispatcher.UIThread.Post(() =>
        {
            RequestTabs.ContainerFromIndex(RequestTabs.SelectedIndex)?.BringIntoView();
            if (RequestTabs.SelectedIndex == 0 && this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(x => x.Name == "TabHeaderScroller") is { } scroll)
                scroll.Offset = new Vector(0, 0);
        }, DispatcherPriority.Loaded);
    }
    private void OnPickerOpened(object? sender, EventArgs args)
    {
        if (sender is Flyout { Content: OpenTabsView view }) Dispatcher.UIThread.Post(view.BeginSearch, DispatcherPriority.Loaded);
    }
    private void OnPickerClose(object? sender, EventArgs args)
    {
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(x => x.Name == "AllTabsButton")?.Flyout?.Hide();
    }
}
