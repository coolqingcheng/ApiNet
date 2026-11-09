using Avalonia.Controls;
using Avalonia.Input;
using ApiNet.Desktop.ViewModels;
namespace ApiNet.Desktop.Views.Capture;

public partial class CapturePanelView : UserControl
{
    public CapturePanelView() => InitializeComponent();
    private void OnOpen(object? sender, TappedEventArgs args)
    {
        if (DataContext is MainWindowViewModel model)
            model.ReplayCaptureCommand.Execute(null);
    }
}
