using Avalonia.Controls;
using ApiNet.Desktop.ViewModels.Settings;
namespace ApiNet.Desktop.Views.Settings;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();
    public SettingsWindow(SettingsViewModel model) : this() { DataContext = model; model.Saved += () => Close(); }
}
