using Avalonia.Controls;
using ApiNet.Desktop.ViewModels.Settings;
namespace ApiNet.Desktop.Views.Settings;

public partial class EnvironmentWindow : Window
{
    public EnvironmentWindow() => InitializeComponent();
    public EnvironmentWindow(EnvironmentEditorViewModel model) : this() { DataContext = model; model.Saved += () => Close(); }
}
