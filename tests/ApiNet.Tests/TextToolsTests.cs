using ApiNet.Infrastructure.Tools;

namespace ApiNet.Tests;

public class TextToolsTests
{
    [Fact]
    public void JsonPreservesPrecisionUnicodeAndReportsInvalidInput()
    {
        const string compact = "{\"id\":90071992547409931234,\"text\":\"你好\",\"ok\":true}";
        var formatted = TextTools.Json(compact, true);
        Assert.Contains('\n', formatted); Assert.Contains("你好", formatted);
        Assert.Equal(compact, TextTools.Json(formatted, false));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => TextTools.Json("{oops}", true));
    }
    [Fact]
    public void UrlAndBase64RoundTripUtf8Text()
    {
        const string text = "中文 a+b &=?%😀";
        Assert.Equal(text, TextTools.UrlDecode(TextTools.UrlEncode(text)));
        Assert.Equal("a b+c", TextTools.UrlDecode("a+b%2Bc"));
        Assert.Equal(text, TextTools.Base64Decode(TextTools.Base64Encode(text)));
        Assert.Throws<FormatException>(() => TextTools.UrlDecode("%2Z"));
        Assert.Throws<FormatException>(() => TextTools.Base64Decode("invalid!"));
        Assert.Throws<System.Text.DecoderFallbackException>(() => TextTools.Base64Decode("/w=="));
    }
}
