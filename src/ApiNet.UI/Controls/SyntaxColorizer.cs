using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using ApiNet.Desktop.Services;

namespace ApiNet.Desktop.Controls;

public sealed partial class SyntaxColorizer(Func<string> language, Func<bool>? enabled = null) : DocumentColorizingTransformer
{
    [GeneratedRegex("\\{\\{\\s*[\\w.\\-]+\\s*\\}\\}")]
    public static partial Regex Variables();
    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"|\\b(?:true|false|null|-?\\d+(?:\\.\\d+)?)\\b")]
    private static partial Regex JsonTokens();
    [GeneratedRegex("</?[\\w:.-]+|/?>|[\\w:.-]+(?=\\s*=)|\"[^\"]*\"|'[^']*'|<!--.*?-->")]
    private static partial Regex XmlTokens();
    protected override void ColorizeLine(DocumentLine line)
    {
        if (enabled?.Invoke() == false) return;
        // Bound work per line so a large minified response remains responsive.
        var text = CurrentContext.Document.GetText(line.Offset, Math.Min(line.Length, 32768));
        if (language() is "JSON" or "JavaScript")
            foreach (Match match in JsonTokens().Matches(text))
            {
                var tail = text.AsSpan(match.Index + match.Length).TrimStart();
                var color = match.Value.StartsWith('"') ? (tail.StartsWith(":" ) ? "SyntaxProperty" : "SyntaxString") : "SyntaxLiteral";
                Paint(line, match, color);
            }
        else if (language() is "XML" or "HTML")
            foreach (Match match in XmlTokens().Matches(text))
                Paint(line, match, match.Value.StartsWith('"') || match.Value.StartsWith('\'') ? "SyntaxString" : "SyntaxTag");
        foreach (Match match in Variables().Matches(text))
            ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element =>
            {
                element.TextRunProperties.SetForegroundBrush(ThemeService.Brush("VariableForeground"));
                element.TextRunProperties.SetBackgroundBrush(ThemeService.Brush("VariableBackground"));
            });
    }
    private void Paint(DocumentLine line, Match match, string color) => ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element => element.TextRunProperties.SetForegroundBrush(ThemeService.Brush(color)));
}
