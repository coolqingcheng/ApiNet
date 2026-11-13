using Avalonia.Controls;
using PostNet.Desktop.ViewModels.Settings;
namespace PostNet.Desktop.Views.Settings;

public partial class EnvironmentWindow : Window
{
    public EnvironmentWindow() => InitializeComponent();
    public EnvironmentWindow(EnvironmentEditorViewModel model) : this() { DataContext = model; model.Saved += () => Close(); }
}
