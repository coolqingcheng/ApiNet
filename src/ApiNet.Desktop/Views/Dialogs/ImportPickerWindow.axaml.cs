using ApiNet.Desktop.ViewModels.Dialogs;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ApiNet.Desktop.Views.Dialogs;

public partial class ImportPickerWindow : Window
{
    public ImportPickerWindow() => InitializeComponent();
    public ImportPickerWindow(ImportPickerViewModel model) : this()
    {
        DataContext = model;
        model.Completed += selection => Close(selection);
    }
}
