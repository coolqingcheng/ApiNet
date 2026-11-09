using Avalonia.Controls;
using Avalonia.Input;
using ApiNet.Desktop.ViewModels;
namespace ApiNet.Desktop.Views.History;

public partial class HistoryPanelView : UserControl
{
    public HistoryPanelView() => InitializeComponent();
    private void OnOpen(object? sender, TappedEventArgs args)
    {
        if (DataContext is MainWindowViewModel model)
            model.RestoreHistoryCommand.Execute(null);
    }
}
