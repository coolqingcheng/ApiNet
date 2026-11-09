using ApiNet.Desktop.ViewModels.Mobile;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ApiNet.Desktop.Views.Mobile;

public partial class MobileMainView : UserControl
{
    public MobileDialogHost DialogHost => Dialogs;
    public MobileMainView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => { if (DataContext is MobileShellViewModel model) { model.ViewportWidth = Bounds.Width; model.ViewportHeight = Bounds.Height; } };
    }
    private void CloseDrawer(object? sender, TappedEventArgs args) { if (DataContext is MobileShellViewModel model) model.DrawerOpen = false; }
    public bool HandleBack()
    {
        if (Dialogs.Dismiss()) return true;
        if (DataContext is MobileShellViewModel { DrawerOpen: true } model) { model.DrawerOpen = false; return true; }
        return false;
    }
}
