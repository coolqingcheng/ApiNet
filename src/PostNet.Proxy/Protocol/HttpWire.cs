using System.Globalization;
using System.Text;

namespace PostNet.Proxy.Protocol;

internal sealed record WireHead(string StartLine, List<KeyValuePair<string, string>> Headers)
{
    public string? Header(string name) => Headers.FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    public string Display => string.Join('\n', Headers.Select(x => x.Key + ": " + x.Value));
}

internal static class HttpWire
{
    public static async Task<WireHead?> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var data = new MemoryStream();
        var single = new byte[1];
        var matched = 0;
        byte[] end = [13, 10, 13, 10];
        while (data.Length < 65536)
        {
            if (await stream.ReadAsync(single, cancellationToken) == 0)
                return data.Length == 0 ? null : throw new IOException("HTTP Header 未完整接收。");
            data.WriteByte(single[0]);
            matched = single[0] == end[matched] ? matched + 1 : single[0] == 13 ? 1 : 0;
            if (matched == 4)
            {
                var lines = Encoding.Latin1.GetString(data.ToArray()).Split("\r\n");
                List<KeyValuePair<string, string>> headers = [];
                foreach (var line in lines.Skip(1).Where(x => x.Length > 0))
                {
                    var split = line.IndexOf(':');
                    if (split <= 0)
                        throw new IOException("无效 HTTP Header。");
                    headers.Add(new(line[..split].Trim(), line[(split + 1)..].Trim()));
                }
                return new WireHead(lines[0], headers);
            }
        }
        throw new IOException("HTTP Header 超过 64 KB。");
    }

    public static async Task<byte[]> ReadBodyAsync(Stream stream, WireHead head, CancellationToken cancellationToken)
    {
        const int limit = 16 * 1024 * 1024;
        if (head.Header("Transfer-Encoding")?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var result = new MemoryStream();
            while (true)
            {
                var line = await ReadLineAsync(stream, cancellationToken);
                var length = int.Parse(line.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (length < 0 || result.Length + length > limit)
                    throw new IOException("请求体超过 16 MB。");
                if (length == 0)
                {
                    while ((await ReadLineAsync(stream, cancellationToken)).Length > 0)
                    {
                    }
                    break;
                }
                var chunk = new byte[length];
                await stream.ReadExactlyAsync(chunk, cancellationToken);
                result.Write(chunk);
                if ((await ReadLineAsync(stream, cancellationToken)).Length != 0)
                    throw new IOException("无效 chunk 分隔符。");
            }
            return result.ToArray();
        }
        if (!long.TryParse(head.Header("Content-Length"), out var count) || count == 0)
            return [];
        if (count < 0 || count > limit)
            throw new IOException("请求体超过 16 MB。");
        var bytes = new byte[(int)count];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var data = new MemoryStream();
        var single = new byte[1];
        while (data.Length < 8192)
        {
            await stream.ReadExactlyAsync(single, cancellationToken);
            data.WriteByte(single[0]);
            if (single[0] == 10)
                return Encoding.ASCII.GetString(data.ToArray()).TrimEnd('\r', '\n');
        }
        throw new IOException("HTTP 行过长。");
    }

    public static async Task WriteAsync(Stream stream, string text, CancellationToken cancellationToken) => await stream.WriteAsync(Encoding.Latin1.GetBytes(text), cancellationToken);
}
