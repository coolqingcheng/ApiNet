using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using ApiNet.Desktop.Services;

namespace ApiNet.Desktop.Controls;

public sealed class CodeEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CodeEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> LanguageProperty = AvaloniaProperty.Register<CodeEditor, string>(nameof(Language), "Text");
    public static readonly StyledProperty<bool> SingleLineProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(SingleLine));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<bool> WordWrapProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(WordWrap));
    public static readonly StyledProperty<bool> IsTableCellProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsTableCell));
    public static readonly StyledProperty<bool> IsMutedProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsMuted));
    public static readonly StyledProperty<string> WatermarkProperty = AvaloniaProperty.Register<CodeEditor, string>(nameof(Watermark), "");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Language { get => GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
    public bool SingleLine { get => GetValue(SingleLineProperty); set => SetValue(SingleLineProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public bool WordWrap { get => GetValue(WordWrapProperty); set => SetValue(WordWrapProperty, value); }
    public bool IsTableCell { get => GetValue(IsTableCellProperty); set => SetValue(IsTableCellProperty, value); }
    public bool IsMuted { get => GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }
    public string Watermark { get => GetValue(WatermarkProperty); set => SetValue(WatermarkProperty, value); }
    public TextEditor Editor { get; }
    private readonly SyntaxColorizer _colorizer;
    private readonly Border _border;
    private readonly TextBlock _watermark;
    private bool _updating;

    public CodeEditor()
    {
        Editor = new TextEditor
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, Menlo, monospace"),
            FontSize = 13, Background = Brushes.Transparent,
            ShowLineNumbers = true, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.FocusAdorner = null;
        Editor.TextArea.FocusAdorner = null;
        // URL and email detection paints hard-coded blue link runs over the
        // editor foreground, making network addresses unreadable in the dark UI.
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        _colorizer = new SyntaxColorizer(() => Language);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _watermark = new TextBlock { IsHitTestVisible = false, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var content = new Grid(); content.Children.Add(Editor); content.Children.Add(_watermark);
        _border = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Child = content };
        Content = _border;
        GotFocus += (_, _) => ApplyTheme();
        LostFocus += (_, _) => ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        Editor.TextChanged += (_, _) =>
        {
            if (_updating) return;
            var value = SingleLine ? Editor.Text.ReplaceLineEndings("") : Editor.Text;
            if (value != Editor.Text) Editor.Text = value;
            SetCurrentValue(TextProperty, value);
            UpdateWatermark();
        };
        Editor.KeyDown += (_, args) => { if (SingleLine && args.Key == Key.Enter) args.Handled = true; };
        Editor.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!SingleLine || args.Key != Key.Tab || args.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;
            if (TopLevel.GetTopLevel(this) is IInputRoot root)
            {
                root.KeyboardNavigationHandler?.Move(Editor.TextArea, args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NavigationDirection.Previous : NavigationDirection.Next, args.KeyModifiers);
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Editor is null) return;
        if (change.Property == TextProperty && Editor.Text != Text)
        {
            _updating = true;
            Editor.Text = Text ?? "";
            _updating = false;
            UpdateWatermark();
        }
        if (change.Property == LanguageProperty) Editor.TextArea.TextView.Redraw();
        if (change.Property == IsReadOnlyProperty) Editor.IsReadOnly = IsReadOnly;
        if (change.Property == WordWrapProperty || change.Property == SingleLineProperty)
        {
            Editor.WordWrap = WordWrap && !SingleLine;
            Editor.HorizontalScrollBarVisibility = SingleLine ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : WordWrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }
        if (change.Property == SingleLineProperty || change.Property == IsTableCellProperty)
        {
            Editor.ShowLineNumbers = !SingleLine;
            // Hidden still scrolls: a short cell's caret and mouse selection can
            // recursively scroll each other. Single-line inputs never scroll vertically.
            Editor.VerticalScrollBarVisibility = SingleLine ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            _border.Padding = IsTableCell ? new Thickness(10, 6) : SingleLine ? new Thickness(12, 8) : new Thickness(12);
            _border.BorderThickness = SingleLine ? new Thickness(1) : new Thickness(0);
            _border.CornerRadius = IsTableCell ? new CornerRadius(0) : new CornerRadius(4);
            Editor.FontSize = IsTableCell ? 12 : 13;
            Editor.FontFamily = IsTableCell ? new FontFamily("Inter, Microsoft YaHei UI, Noto Sans CJK SC, sans-serif") : new FontFamily("Cascadia Code, Consolas, Menlo, monospace");
            Editor.VerticalAlignment = SingleLine ? Avalonia.Layout.VerticalAlignment.Center : Avalonia.Layout.VerticalAlignment.Stretch;
            ApplyTheme();
        }
        if (change.Property == IsMutedProperty) ApplyTheme();
        if (change.Property == IsKeyboardFocusWithinProperty) ApplyTheme();
        if (change.Property == WatermarkProperty) UpdateWatermark();
    }
    private void UpdateWatermark()
    {
        if (_watermark is null) return;
        _watermark.Text = Watermark;
        _watermark.IsVisible = Text.Length == 0 && Watermark.Length > 0;
    }
    private void ApplyTheme()
    {
        if (_border is null) return;
        Editor.Foreground = ThemeService.Brush(IsMuted ? "TablePlaceholder" : IsTableCell ? "TableText" : "CodeForeground");
        _watermark.Foreground = ThemeService.Brush("TablePlaceholder");
        _watermark.FontFamily = Editor.FontFamily; _watermark.FontSize = Editor.FontSize;
        Editor.LineNumbersForeground = ThemeService.Brush("TextMuted");
        Editor.TextArea.Caret.CaretBrush = ThemeService.Brush("TextPrimary");
        Editor.TextArea.SelectionBrush = ThemeService.Brush("Selection");
        Editor.TextArea.SelectionForeground = ThemeService.Brush("SelectionText");
        _border.Background = IsTableCell ? Brushes.Transparent : ThemeService.Brush("WindowBackground");
        _border.BorderBrush = IsKeyboardFocusWithin ? ThemeService.Brush(IsTableCell ? "TableFocus" : "Accent") : IsTableCell ? Brushes.Transparent : ThemeService.Brush("Border");
        Editor.TextArea.TextView.Redraw();
    }
}
