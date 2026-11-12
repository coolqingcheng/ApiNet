using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ApiNet.Infrastructure.Tools;

namespace ApiNet.Tests;

public class ResponseTextFormatterTests
{
    [Fact]
    public void JsonFormattingAndCompressionPreserveNumbersStringsAndRootValues()
    {
        const string text = "{\"id\":90071992547409931234,\"text\":\"中文 a  b\\nnext\",\"array\":[true,null,-1.2300e+20]}";
        var pretty = ResponseTextFormatter.Transform(text, "JSON", true);
        Assert.Contains('\n', pretty);
        Assert.Equal(text, ResponseTextFormatter.Transform(pretty, "JSON", false));
        Assert.Equal("[\n  1,\n  2,\n  {\n    \"n\": 3\n  }\n]", TextTools.Json("[1,2,{\"n\":3}]", true));
        const string escaped = "{\"\\u4e2d\\\"\":\"\\u6587\\\\slash\\nline\"}";
        Assert.Equal(escaped, TextTools.Json(TextTools.Json(escaped, true), false));
        foreach (var value in new[] { "true", "null", "12345678901234567890", "\"a  b\"", "[]" }) Assert.Equal(value, TextTools.Json(value, false));
        Assert.ThrowsAny<JsonException>(() => TextTools.Json("{\"id\":", true));
        Assert.ThrowsAny<JsonException>(() => TextTools.Json("{} {}", true));
        Assert.ThrowsAny<JsonException>(() => TextTools.Json(" ", true));
    }

    [Fact]
    public void XmlCompressionKeepsMixedContentAndExplicitWhitespace()
    {
        const string text = "<root>\n  <p>Hello <b>world</b> !</p>\n  <raw xml:space=\"preserve\">  a\n b  </raw>\n</root>";
        var compact = ResponseTextFormatter.Transform(text, "XML", false);
        Assert.Contains("<p>Hello <b>world</b> !</p>", compact);
        Assert.Equal(XDocument.Parse(text).Root!.Element("raw")!.Value, XDocument.Parse(compact).Root!.Element("raw")!.Value);
        var pretty = ResponseTextFormatter.Transform("<root><a>1</a><b>2</b></root>", "XML", true);
        Assert.Contains('\n', pretty);
        Assert.Equal("<root><a>1</a><b>2</b></root>", ResponseTextFormatter.Transform(pretty, "XML", false));
        Assert.Throws<XmlException>(() => ResponseTextFormatter.Transform("<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///unreadable'>]><root>&x;</root>", "XML", true));
    }

    [Fact]
    public void HtmlFormattingSupportsHtml5AndKeepsPreformattedAndScriptText()
    {
        const string text = "<!doctype html><html><body><input disabled><div><p>Hello <b>world</b> !</p><pre>  a\n b  </pre><script>const s = 'a  b';</script></div></body></html>";
        var pretty = ResponseTextFormatter.Transform(text, "HTML", true);
        Assert.Contains('\n', pretty);
        var compact = ResponseTextFormatter.Transform(pretty, "HTML", false);
        Assert.Contains("<input", compact);
        Assert.Contains("  a\n b  ", compact);
        Assert.Contains("const s = 'a  b';", compact);
    }

    [Theory]
    [InlineData("JavaScript", "function add(first, second) { const label = 'a  b'; return first + second + label; }")]
    [InlineData("CSS", ".notice { content: 'a  b'; margin: 0  5px; }")]
    public void ScriptAndCssCompressionKeepsStringWhitespace(string format, string text)
    {
        var pretty = ResponseTextFormatter.Transform(text, format, true);
        Assert.Contains('\n', pretty);
        var compact = ResponseTextFormatter.Transform(pretty, format, false);
        Assert.Contains("a  b", compact);
        Assert.DoesNotContain('\n', compact);
        if (format == "JavaScript") { Assert.Contains("first", compact); Assert.Contains("second", compact); }
    }

    [Theory]
    [InlineData("APPLICATION/PROBLEM+JSON; charset=utf-8", "error", "JSON")]
    [InlineData("text/plain", "  {\"ok\":true}", "JSON")]
    [InlineData("text/plain", "<!doctype html><p>hello</p>", "HTML")]
    [InlineData("application/octet-stream", "<root/>", "XML")]
    [InlineData("text/javascript", "const x=1;", "JavaScript")]
    [InlineData("text/css", "body {}", "CSS")]
    [InlineData("text/plain", "hello world", "Text")]
    public void FormatsAreDetectedFromMimeTypeOrStructuredText(string type, string text, string expected) => Assert.Equal(expected, ResponseTextFormatter.DetectFormat(type, text));

    [Fact]
    public void LargeJsonCanBeCanceledAndRetainsEveryItemWhenCompleted()
    {
        var text = "[" + string.Join(',', Enumerable.Repeat("{\"id\":90071992547409931234,\"text\":\"中文 a  b\"}", 30000)) + "]";
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => TextTools.Json(text, true, canceled.Token));
        var pretty = TextTools.Json(text, true);
        using var document = JsonDocument.Parse(pretty);
        Assert.Equal(30000, document.RootElement.GetArrayLength());
        Assert.Equal("中文 a  b", document.RootElement[29999].GetProperty("text").GetString());
        Assert.Equal(text, TextTools.Json(pretty, false));
    }
}
