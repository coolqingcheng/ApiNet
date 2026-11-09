using Avalonia.Controls;
namespace ApiNet.Desktop.Views.Mobile;
public partial class MobileRequestView : UserControl
{
    public MobileRequestView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => NameRow.IsVisible = Bounds.Height >= 350 || NameInput.IsKeyboardFocusWithin;
    }
}
