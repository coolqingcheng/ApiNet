using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;

namespace ApiNet.Desktop.Controls;

public sealed class CodeEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CodeEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> LanguageProperty = AvaloniaProperty.Register<CodeEditor, string>(nameof(Language), "Text");
    public static readonly StyledProperty<bool> SingleLineProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(SingleLine));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsReadOnly));
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Language { get => GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
    public bool SingleLine { get => GetValue(SingleLineProperty); set => SetValue(SingleLineProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public TextEditor Editor { get; }
    private readonly SyntaxColorizer _colorizer;
    private readonly Border _border;
    private bool _updating;

    public CodeEditor()
    {
        Editor = new TextEditor
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, Menlo, monospace"),
            FontSize = 13, Background = Brushes.Transparent, Foreground = Brush.Parse("#D4D4D4"),
            ShowLineNumbers = true, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Editor.Options.ConvertTabsToSpaces = true;
        _colorizer = new SyntaxColorizer(() => Language);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _border = new Border { Background = Brush.Parse("#1D1F26"), BorderBrush = Brush.Parse("#383B46"), CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Child = Editor };
        Content = _border;
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
        if (change.Property == SingleLineProperty)
        {
            Editor.ShowLineNumbers = !SingleLine;
            Editor.VerticalScrollBarVisibility = SingleLine ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            Editor.HorizontalScrollBarVisibility = SingleLine ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            _border.Padding = SingleLine ? new Thickness(10, 9) : new Thickness(12);
            _border.BorderThickness = SingleLine ? new Thickness(1) : new Thickness(0);
        }
    }
}
