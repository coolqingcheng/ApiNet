using ApiNet.Infrastructure.Tools;

foreach (var (format, text) in new[] {
    ("JSON", "{\"message\":\"中文 a  b\",\"id\":90071992547409931234,\"items\":[1,2,3]}"),
    ("XML", "<root><item>中文</item></root>"),
    ("HTML", "<!doctype html><html><body><input disabled><div><p>Hello</p><script>const s = 'a  b';</script></div></body></html>"),
    ("JavaScript", "function add(first, second) { const label = 'a  b'; return first + second + label; }"),
    ("CSS", ".notice { content: 'a  b'; margin: 0  5px; }")
})
{
    var pretty = ResponseTextFormatter.Transform(text, format, true);
    var compact = ResponseTextFormatter.Transform(pretty, format, false);
    if (string.IsNullOrEmpty(compact) || !pretty.Contains('\n')) throw new Exception(format + " failed");
    if (format == "JSON" && compact != text) throw new Exception("JSON content changed.");
    Console.WriteLine(format + " native-aot-format-and-minify=True");
}
try { ResponseTextFormatter.Transform("function (", "JavaScript", true); throw new Exception("Expected a format error."); }
catch (FormatException exception) { Console.WriteLine("native-aot-invalid-js-error=" + exception.Message); }
