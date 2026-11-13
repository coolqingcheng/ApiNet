using PostNet.Desktop.ViewModels.Tabs;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels.Mobile;

public partial class MobileShellViewModel : ViewModelBase
{
    public MainWindowViewModel Main { get; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsCompact), nameof(SidebarVisible), nameof(SidebarWidth), nameof(ContentMargin), nameof(ShowShade))] private double _viewportWidth = 390;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsShort), nameof(ShowContext), nameof(IsCompact), nameof(SidebarVisible), nameof(ContentMargin), nameof(ShowShade))] private double _viewportHeight = 844;
    public bool IsShort => ViewportHeight < 550;
    public bool ShowContext => !IsShort;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SidebarVisible), nameof(ShowShade))] private bool _drawerOpen;
    public bool IsCompact => ViewportWidth < 850 || IsShort;
    public bool SidebarVisible => !IsCompact || DrawerOpen;
    public bool ShowShade => IsCompact && DrawerOpen;
    public double SidebarWidth => Math.Clamp(ViewportWidth - 32, 240, 330);
    public Thickness ContentMargin => new(IsCompact ? 0 : SidebarWidth, 0, 0, 0);
    public MobileShellViewModel(MainWindowViewModel main)
    {
        Main = main;
        Main.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Main.SelectedTab) && Main.SelectedTab is not null) DrawerOpen = false; };
    }
    [RelayCommand] private void ToggleDrawer() => DrawerOpen = !DrawerOpen;
    [RelayCommand] private void CloseDrawer() => DrawerOpen = false;
    [RelayCommand] private void Navigate(string panel)
    {
        Main.NavigateCommand.Execute(panel);
        if (panel == "工具") DrawerOpen = false;
    }
    [RelayCommand] private async Task NewHttpAsync() { await Main.NewHttpCommand.ExecuteAsync(null); Main.ActivePanel = "工作区"; DrawerOpen = false; }
    [RelayCommand] private async Task NewSocketAsync() { await Main.NewSocketCommand.ExecuteAsync(null); Main.ActivePanel = "工作区"; DrawerOpen = false; }
}
