using Avalonia.Controls;
using PostNet.Desktop.ViewModels.Settings;
namespace PostNet.Desktop.Views.Settings;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();
    public SettingsWindow(SettingsViewModel model) : this() { DataContext = model; model.Saved += () => Close(); }
}
