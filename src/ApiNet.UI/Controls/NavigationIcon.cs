using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace ApiNet.Desktop.Controls;

public enum NavigationIconKind { Workspace, History, Capture, Tools, Theme, Update, Settings, Folder }

// Fixed vector viewports avoid font fallback and glyph clipping on the first frame.
public sealed class NavigationIcon : TemplatedControl
{
    public static readonly StyledProperty<NavigationIconKind> KindProperty = AvaloniaProperty.Register<NavigationIcon, NavigationIconKind>(nameof(Kind));
    public NavigationIconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private static readonly Geometry[] Shapes =
    [
        Geometry.Parse("M4,3 L20,3 L20,21 L4,21 Z M8,7 L16,7 M8,12 L16,12 M8,17 L14,17"),
        Geometry.Parse("M3,11 A9,9 0 1 1 5,18 M3,4 L3,11 L10,11 M12,7 L12,12 L16,14"),
        Geometry.Parse("M3,7 L21,7 M16,2 L21,7 L16,12 M21,17 L3,17 M8,12 L3,17 L8,22"),
        Geometry.Parse("M14,5 A5,5 0 0 0 8,11 L3,16 A3,3 0 0 0 8,21 L13,16 A5,5 0 0 0 19,10 L15,12 L12,9 Z"),
        Geometry.Parse("M12,3 A9,9 0 1 1 12,21 A9,9 0 1 1 12,3 Z M12,3 L12,21 M16,7 L16,17 M19,10 L19,14"),
        Geometry.Parse("M12,16 L12,3 M7,8 L12,3 L17,8 M4,15 L4,21 L20,21 L20,15"),
        Geometry.Parse("M10,2 L14,2 L15,5 L18,6 L21,5 L23,9 L21,11 L21,14 L23,16 L21,20 L18,19 L15,20 L14,23 L10,23 L9,20 L6,19 L3,20 L1,16 L3,14 L3,11 L1,9 L3,5 L6,6 L9,5 Z M12,8 A4.5,4.5 0 1 1 12,17 A4.5,4.5 0 1 1 12,8 Z"),
        Geometry.Parse("M3,6 L10,6 L12,9 L21,9 L21,20 L3,20 Z")
    ];
    static NavigationIcon() => AffectsRender<NavigationIcon>(KindProperty, ForegroundProperty);
    public NavigationIcon() { Width = 24; Height = 24; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 26;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2));
        context.DrawGeometry(null, new Pen(Foreground, 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Shapes[(int)Kind]);
    }
}
