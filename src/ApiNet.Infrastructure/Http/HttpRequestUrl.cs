namespace ApiNet.Infrastructure.Http;

public static class HttpRequestUrl
{
    public static string WithDefaultScheme(string url)
    {
        url = url.Trim();
        if (url.Length == 0) return url;
        if (url.StartsWith("//", StringComparison.Ordinal)) return "http:" + url;
        // An authority such as localhost:8080 is a host and port, not a URI scheme.
        var separator = url.IndexOf("://", StringComparison.Ordinal);
        if (separator > 0 && Uri.CheckSchemeName(url[..separator])) return url;
        return "http://" + url;
    }
}
