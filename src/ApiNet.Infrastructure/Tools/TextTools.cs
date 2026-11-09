using System.Text;
using System.Text.Json;

namespace ApiNet.Infrastructure.Tools;

public static class TextTools
{
    public static string Json(string input, bool indent)
    {
        using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 128 });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indent, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) document.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string UrlEncode(string input) => Uri.EscapeDataString(input);
    public static string UrlDecode(string input)
    {
        for (var i = 0; i < input.Length; i++)
            if (input[i] == '%' && (i + 2 >= input.Length || !Uri.IsHexDigit(input[i + 1]) || !Uri.IsHexDigit(input[i + 2]))) throw new FormatException("URL 百分号编码无效。每个 % 后需要两位十六进制数字。");
        return Uri.UnescapeDataString(input.Replace('+', ' '));
    }
    public static string Base64Encode(string input) => Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
    public static string Base64Decode(string input) => new UTF8Encoding(false, true).GetString(Convert.FromBase64String(input));
}
