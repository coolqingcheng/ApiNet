namespace PostNet.Infrastructure.Http;

public static class HeaderParser
{
    public static IEnumerable<KeyValuePair<string, string>> Parse(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;
            var split = line.IndexOf(':');
            if (split <= 0)
                throw new FormatException($"Header 格式应为 名称: 值：{line}");
            yield return new(line[..split].Trim(), line[(split + 1)..].Trim());
        }
    }
}
