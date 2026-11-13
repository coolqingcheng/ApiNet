using System.Text.RegularExpressions;

namespace PostNet.Core.Models;

public static partial class QueryUrl
{
    public static List<RequestParameter> Parameters(RequestDefinition request) => [.. Parse(request.Url), .. request.QueryParameters];
    public static void Normalize(RequestDefinition request)
    {
        request.QueryParameters = Parameters(request);
        request.Url = WithoutQuery(request.Url);
    }

    public static string WithoutQuery(string url)
    {
        var hash = url.IndexOf('#');
        var query = url.IndexOf('?');
        return query >= 0 && (hash < 0 || query < hash) ? url[..query] + (hash >= 0 ? url[hash..] : "") : url;
    }

    public static List<RequestParameter> Parse(string url)
    {
        var hash = url.IndexOf('#');
        var query = url.IndexOf('?');
        if (query < 0 || (hash >= 0 && query > hash)) return [];
        return url[(query + 1)..(hash >= 0 ? hash : url.Length)].Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var pair = part.Split('=', 2);
            return new RequestParameter { Key = Decode(pair[0]), Value = pair.Length > 1 ? Decode(pair[1]) : "" };
        }).ToList();
    }

    public static string WithQuery(string url, IEnumerable<RequestParameter> rows)
    {
        var baseUrl = WithoutQuery(url);
        var hash = baseUrl.IndexOf('#');
        var fragment = hash >= 0 ? baseUrl[hash..] : "";
        var path = hash >= 0 ? baseUrl[..hash] : baseUrl;
        var query = string.Join('&', rows.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)).Select(x => Encode(x.Key) + "=" + Encode(x.Value)));
        return path + (query.Length > 0 ? "?" + query : "") + fragment;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
    private static string Encode(string value)
    {
        var result = new System.Text.StringBuilder();
        var start = 0;
        foreach (Match match in Placeholder().Matches(value))
        {
            result.Append(Uri.EscapeDataString(value[start..match.Index]));
            result.Append(match.Value);
            start = match.Index + match.Length;
        }
        result.Append(Uri.EscapeDataString(value[start..]));
        return result.ToString();
    }

    [GeneratedRegex(@"\{\{[^{}]+\}\}")]
    private static partial Regex Placeholder();
}
