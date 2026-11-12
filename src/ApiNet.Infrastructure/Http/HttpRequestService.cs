using System.Diagnostics;
using System.Net;
using System.Text;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;

namespace ApiNet.Infrastructure.Http;

public sealed class HttpRequestService : IHttpRequestService
{
    public const int MaximumBodyBytes = 4 * 1024 * 1024;
    public async Task<HttpResult> SendAsync(RequestDefinition request, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        string? downloadPath = null;
        try
        {
            if (!Uri.TryCreate(HttpRequestUrl.WithDefaultScheme(request.Url), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new FormatException("请输入有效的 http:// 或 https:// 地址。");
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = request.FollowRedirects, AutomaticDecompression = DecompressionMethods.All };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 3600)) };
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
            if (request.BodyFormat == "Form URL Encoded")
            {
                if (request.BodyParameters.Any(x => x.Enabled && x.Type == "File")) throw new FormatException("文件上传请选择 Multipart 请求体类型。");
                message.Content = new FormUrlEncodedContent(request.BodyParameters.Where(x => x.Enabled && x.Key.Length > 0).Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
            }
            else if (request.BodyFormat == "Multipart")
            {
                var multipart = new MultipartFormDataContent();
                message.Content = multipart;
                foreach (var parameter in request.BodyParameters.Where(x => x.Enabled && x.Key.Length > 0))
                {
                    if (parameter.Type == "File")
                    {
                        if (string.IsNullOrWhiteSpace(parameter.Value) || !File.Exists(parameter.Value)) throw new FormatException("上传文件不存在，请重新选择文件：" + parameter.Key);
                        var file = new StreamContent(new FileStream(parameter.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan));
                        try
                        {
                            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                            multipart.Add(file, parameter.Key, Path.GetFileName(parameter.Value));
                        }
                        catch { file.Dispose(); throw; }
                    }
                    else multipart.Add(new StringContent(parameter.Value, Encoding.UTF8), parameter.Key);
                }
            }
            else if (!string.IsNullOrEmpty(request.Body))
                message.Content = new StringContent(request.Body, Encoding.UTF8, BodyContentType(request.BodyFormat));
            foreach (var header in HeaderParser.Parse(request.Headers))
            {
                // MultipartFormDataContent owns the boundary; imported bare media types must not replace it.
                if (message.Content is MultipartFormDataContent && header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    message.Content ??= new ByteArrayContent([]);
                    message.Content.Headers.Remove(header.Key);
                    if (!message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                        throw new FormatException($"无效 Header：{header.Key}");
                }
            }
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 3600)));
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            var downloadDirectory = Path.Combine(Path.GetTempPath(), "ApiNet", "responses");
            Directory.CreateDirectory(downloadDirectory);
            downloadPath = Path.Combine(downloadDirectory, Guid.NewGuid().ToString("N"));
            long size = 0;
            var buffer = new byte[16384];
            await using (var file = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, timeout.Token);
                    if (count == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    size += count;
                    if (body.Length < MaximumBodyBytes)
                        body.Write(buffer, 0, (int)Math.Min(count, MaximumBodyBytes - body.Length));
                }
            }
            var bytes = body.ToArray();
            var truncated = size > MaximumBodyBytes;
            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            var disposition = response.Content.Headers.ContentDisposition;
            var attachment = disposition?.DispositionType.Equals("attachment", StringComparison.OrdinalIgnoreCase) == true || disposition?.FileName is not null || disposition?.FileNameStar is not null;
            var binary = media.Length > 0 && !(media.StartsWith("text/") || media.Contains("json") || media.Contains("xml") || media.Contains("javascript") || media == "application/x-www-form-urlencoded");
            binary |= bytes.Contains((byte)0);
            var fileName = attachment || binary ? SafeFileName(disposition?.FileNameStar ?? disposition?.FileName ?? Path.GetFileName(uri.LocalPath)) : null;
            Encoding encoding;
            try { encoding = Encoding.GetEncoding(response.Content.Headers.ContentType?.CharSet?.Trim('"') ?? "utf-8"); }
            catch (ArgumentException) { encoding = Encoding.UTF8; }
            return new HttpResult
            {
                StatusCode = (int)response.StatusCode,
                StatusText = response.ReasonPhrase ?? "",
                Headers = string.Join('\n', response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")),
                Body = binary ? $"文件响应：{fileName}\n类型：{media}\n大小：{size:N0} 字节\n完整内容已接收，可保存到本地。" : encoding.GetString(bytes),
                ContentType = media,
                FileName = fileName,
                DownloadPath = downloadPath,
                Size = size,
                DurationMs = timer.ElapsedMilliseconds,
                Truncated = truncated
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or FormatException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            if (downloadPath is not null) File.Delete(downloadPath);
            return new HttpResult { Error = exception is OperationCanceledException ? (cancellationToken.IsCancellationRequested ? "请求已取消" : "请求超时") : exception.Message, DurationMs = timer.ElapsedMilliseconds };
        }
    }
    public static string BodyContentType(string format) => format switch { "JSON" => "application/json", "XML" => "application/xml", "HTML" => "text/html", "JavaScript" => "application/javascript", "Form URL Encoded" => "application/x-www-form-urlencoded", "Multipart" => "multipart/form-data", _ => "text/plain" };
    public static string SafeFileName(string? value)
    {
        value = (value ?? "").Trim('"').Replace('\\', '/').Split('/').Last();
        var name = new string(value.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 160) name = name[..160];
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "response.bin";
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[1-9]$")) name = "_" + name;
        return name;
    }
}
