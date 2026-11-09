using Avalonia.Controls;
using ApiNet.Desktop.ViewModels.Dialogs;
namespace ApiNet.Desktop.Views.Dialogs;

public partial class DialogWindow : Window
{
    private bool _completed;
    public DialogWindow() => InitializeComponent();
    public DialogWindow(DialogViewModel model) : this()
    {
        DataContext = model;
        model.Completed += result => { _completed = true; Close(result); };
        Closing += (_, args) => { if (!_completed) { args.Cancel = true; _completed = true; Close(-1); } };
        Opened += (_, _) => { if (model.ShowInput) { Input.Focus(); Input.SelectAll(); } };
    }
}
