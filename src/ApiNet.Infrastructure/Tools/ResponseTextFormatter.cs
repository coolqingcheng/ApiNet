using System.Xml;
using System.Xml.Linq;
using NUglify;
using NUglify.Css;
using NUglify.Html;
using NUglify.JavaScript;

namespace ApiNet.Infrastructure.Tools;

public static class ResponseTextFormatter
{
    public static string DetectFormat(string contentType, string text)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return "JSON";
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase)) return "HTML";
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)) return "XML";
        if (contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) || contentType.Contains("ecmascript", StringComparison.OrdinalIgnoreCase)) return "JavaScript";
        if (contentType.Contains("css", StringComparison.OrdinalIgnoreCase)) return "CSS";
        var start = text.AsSpan().TrimStart();
        if (start.StartsWith("{" ) || start.StartsWith("[")) return "JSON";
        if (start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "HTML";
        if (start.StartsWith("<")) return "XML";
        return "Text";
    }

    public static string Transform(string text, string format, bool indent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (format == "JSON") return TextTools.Json(text, indent, cancellationToken);
        if (format == "XML")
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            // Remove layout line breaks only; keep mixed content, inline spaces and xml:space.
            foreach (var element in document.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var space = element.AncestorsAndSelf().Attributes(XNamespace.Xml + "space").FirstOrDefault()?.Value;
                if (space == "preserve" || element.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value))) continue;
                foreach (var whitespace in element.Nodes().OfType<XText>().Where(x => string.IsNullOrWhiteSpace(x.Value) && (x.Value.Contains('\n') || x.Value.Contains('\r'))).ToArray()) whitespace.Remove();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return document.ToString(indent ? SaveOptions.None : SaveOptions.DisableFormatting);
        }
        UglifyResult result;
        if (format == "HTML")
        {
            var settings = HtmlSettings.Pretty();
            settings.PrettyPrint = indent;
            settings.CollapseWhitespaces = !indent;
            result = Uglify.Html(text, settings);
        }
        else if (format == "JavaScript")
        {
            var settings = CodeSettings.Pretty();
            settings.OutputMode = indent ? OutputMode.MultipleLines : OutputMode.SingleLine;
            settings.MinifyCode = false;
            result = Uglify.Js(text, settings);
        }
        else if (format == "CSS")
        {
            var settings = CssSettings.Pretty();
            settings.OutputMode = indent ? OutputMode.MultipleLines : OutputMode.SingleLine;
            result = Uglify.Css(text, settings);
        }
        else throw new FormatException("纯文本没有统一的格式化规则；如果内容是 JSON、XML 等，请先选择对应类型。");
        cancellationToken.ThrowIfCancellationRequested();
        if (result.HasErrors) throw new FormatException(string.Join('\n', result.Errors.Where(x => x.IsError).Take(3).Select(x => x.ToString())));
        return result.Code;
    }
}
