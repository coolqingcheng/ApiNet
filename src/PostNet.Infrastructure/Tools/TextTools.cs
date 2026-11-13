using System.Text;
using System.Text.Json;
using System.Buffers;

namespace PostNet.Infrastructure.Tools;

public static class TextTools
{
    public static string Json(string input, bool indent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes(input);
        cancellationToken.ThrowIfCancellationRequested();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
        // Validate structure while copying token bytes, preserving number precision
        // and string escapes without a DOM or a separate string per JSON value.
        var output = new ArrayBufferWriter<byte>();
        var previous = JsonTokenType.None;
        var tokens = 0;
        while (reader.Read())
        {
            if ((tokens++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var token = reader.TokenType;
            var isValue = token is JsonTokenType.StartObject or JsonTokenType.StartArray or JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null;
            if (token == JsonTokenType.PropertyName)
            {
                if (previous != JsonTokenType.StartObject) output.Write(","u8);
                NewLine(reader.CurrentDepth);
            }
            else if (isValue && previous != JsonTokenType.PropertyName)
            {
                if (previous is JsonTokenType.EndObject or JsonTokenType.EndArray or JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null) output.Write(","u8);
                if (reader.CurrentDepth > 0) NewLine(reader.CurrentDepth);
            }
            else if ((token is JsonTokenType.EndObject or JsonTokenType.EndArray) && previous is not (JsonTokenType.StartObject or JsonTokenType.StartArray)) NewLine(reader.CurrentDepth);
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject: output.Write("{"u8); break;
                case JsonTokenType.EndObject: output.Write("}"u8); break;
                case JsonTokenType.StartArray: output.Write("["u8); break;
                case JsonTokenType.EndArray: output.Write("]"u8); break;
                case JsonTokenType.PropertyName:
                    output.Write(bytes.AsSpan((int)reader.TokenStartIndex, reader.ValueSpan.Length + 2));
                    output.Write(indent ? ": "u8 : ":"u8); break;
                case JsonTokenType.String: output.Write(bytes.AsSpan((int)reader.TokenStartIndex, reader.ValueSpan.Length + 2)); break;
                case JsonTokenType.Number: output.Write(reader.ValueSpan); break;
                case JsonTokenType.True: output.Write("true"u8); break;
                case JsonTokenType.False: output.Write("false"u8); break;
                case JsonTokenType.Null: output.Write("null"u8); break;
            }
            previous = token;
        }
        if (tokens == 0) throw new JsonException("请输入有效的 JSON 内容。");
        cancellationToken.ThrowIfCancellationRequested();
        return Encoding.UTF8.GetString(output.WrittenSpan);

        void NewLine(int depth)
        {
            if (!indent) return;
            output.Write("\n"u8);
            var count = depth * 2;
            output.GetSpan(count)[..count].Fill((byte)' ');
            output.Advance(count);
        }
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
