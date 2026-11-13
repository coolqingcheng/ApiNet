using Avalonia;
using Avalonia.Controls;
namespace PostNet.Desktop.Views.Mobile;
public partial class MobileRequestView : UserControl
{
    public MobileRequestView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Height < 350;
            NameRow.IsVisible = !compact || NameInput.IsKeyboardFocusWithin;
            RequestToolbar.RowDefinitions = new(compact ? "Auto" : "Auto,Auto");
            Grid.SetColumn(UrlEditor, compact ? 1 : 0);
            Grid.SetColumnSpan(UrlEditor, compact ? 1 : 3);
            UrlEditor.Margin = compact ? new Thickness(8, 0) : new Thickness(0, 0, 0, 8);
            Grid.SetRow(MethodPicker, compact ? 0 : 1);
            Grid.SetRow(SendActions, compact ? 0 : 1);
            PreviewButton.IsVisible = !compact;
        };
    }
}
