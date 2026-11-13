using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PostNet.Infrastructure.Imports;

public sealed class OpenApiDocumentDownloader
{
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;
    public async Task<string> ReadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new FormatException("请输入 HTTP 或 HTTPS 的 Swagger / OpenAPI JSON 地址。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, MaxAutomaticRedirections = 5 };
            using var client = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("Post.NET");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"文档下载失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.Content.Headers.ContentLength > MaximumDocumentBytes) throw new FormatException("文档超过 16 MB，请下载后拆分导入。");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, timeout.Token);
                if (count == 0) break;
                if (bytes.Length + count > MaximumDocumentBytes) throw new FormatException("文档超过 16 MB，请下载后拆分导入。");
                bytes.Write(buffer, 0, count);
            }
            Encoding encoding;
            try { encoding = Encoding.GetEncoding(response.Content.Headers.ContentType?.CharSet?.Trim('"') ?? "utf-8"); }
            catch (ArgumentException) { encoding = Encoding.UTF8; }
            var json = encoding.GetString(bytes.ToArray()).TrimStart('\uFEFF');
            JsonObject? root;
            try { root = JsonNode.Parse(json) as JsonObject; }
            catch (JsonException) { throw new FormatException("链接返回的内容不是 JSON 文档。请填写 swagger.json 或 /v3/api-docs 等 JSON 地址，Swagger UI 页面链接不能直接导入。"); }
            if (root is null || root["swagger"] is null && root["openapi"] is null) throw new FormatException("链接内容不是 Swagger / OpenAPI 文档，请检查 JSON 地址。");
            return json;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("文档下载超时，请检查地址和网络后重试。"); }
    }
}
