using ApiNet.Desktop.ViewModels.Tabs;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ApiNet.Desktop.Views.Tabs;

public partial class OpenTabsView : UserControl
{
    public event EventHandler? RequestClose;
    public OpenTabsView() => InitializeComponent();
    public void BeginSearch()
    {
        if (DataContext is OpenTabsViewModel model) model.Search = "";
        TabResults.SelectedItem = null;
        TabSearch.Focus();
    }
    private void ActivateSelected()
    {
        if (DataContext is OpenTabsViewModel model && (TabResults.SelectedItem as RequestTabViewModel ?? model.Matches.FirstOrDefault()) is { } tab)
        { model.Activate(tab); RequestClose?.Invoke(this, EventArgs.Empty); }
    }
    private void OnResultTapped(object? sender, TappedEventArgs args)
    {
        if (args.Source is Control source && (source is ListBoxItem || source.FindAncestorOfType<ListBoxItem>() is not null) && TabResults.SelectedItem is not null) ActivateSelected();
    }
    private void OnResultKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { ActivateSelected(); args.Handled = true; }
    }
    private void OnSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { ActivateSelected(); args.Handled = true; }
        else if (args.Key == Key.Down && TabResults.ItemCount > 0)
        { TabResults.SelectedIndex = 0; TabResults.ContainerFromIndex(0)?.Focus(); args.Handled = true; }
    }
}
