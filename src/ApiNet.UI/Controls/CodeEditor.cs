using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
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
    public static readonly StyledProperty<bool> IsTableCellProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsTableCell));
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Language { get => GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
    public bool SingleLine { get => GetValue(SingleLineProperty); set => SetValue(SingleLineProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public bool IsTableCell { get => GetValue(IsTableCellProperty); set => SetValue(IsTableCellProperty, value); }
    public TextEditor Editor { get; }
    private readonly SyntaxColorizer _colorizer;
    private readonly Border _border;
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
        // URL and email detection paints hard-coded blue link runs over the
        // editor foreground, making network addresses unreadable in the dark UI.
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        _colorizer = new SyntaxColorizer(() => Language);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _border = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Child = Editor };
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
        };
        Editor.KeyDown += (_, args) => { if (SingleLine && args.Key == Key.Enter) args.Handled = true; };
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
        }
        if (change.Property == LanguageProperty) Editor.TextArea.TextView.Redraw();
        if (change.Property == IsReadOnlyProperty) Editor.IsReadOnly = IsReadOnly;
        if (change.Property == SingleLineProperty || change.Property == IsTableCellProperty)
        {
            Editor.ShowLineNumbers = !SingleLine;
            Editor.VerticalScrollBarVisibility = SingleLine ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            Editor.HorizontalScrollBarVisibility = SingleLine ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            _border.Padding = SingleLine ? new Thickness(12, 8) : new Thickness(12);
            _border.BorderThickness = SingleLine ? new Thickness(1) : new Thickness(0);
            _border.CornerRadius = IsTableCell ? new CornerRadius(0) : new CornerRadius(4);
            ApplyTheme();
        }
    }
    private void ApplyTheme()
    {
        if (_border is null) return;
        Editor.Foreground = ThemeService.Brush("CodeForeground");
        Editor.LineNumbersForeground = ThemeService.Brush("TextMuted");
        Editor.TextArea.Caret.CaretBrush = ThemeService.Brush("TextPrimary");
        Editor.TextArea.SelectionBrush = ThemeService.Brush("Selection");
        Editor.TextArea.SelectionForeground = ThemeService.Brush("SelectionText");
        _border.Background = IsTableCell ? Brushes.Transparent : ThemeService.Brush("WindowBackground");
        _border.BorderBrush = IsKeyboardFocusWithin ? ThemeService.Brush("Accent") : IsTableCell ? Brushes.Transparent : ThemeService.Brush("Border");
        Editor.TextArea.TextView.Redraw();
    }
}
