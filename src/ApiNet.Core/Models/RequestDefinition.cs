namespace ApiNet.Core.Models;

public sealed class RequestDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? FolderId
    {
        get; set;
    }
    public string Name { get; set; } = "新建请求";
    public string Protocol { get; set; } = "HTTP";
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "{{baseUrl}}/";
    public string Headers { get; set; } = "";
    public string Body { get; set; } = "";
    public string BodyFormat { get; set; } = "JSON";
    public List<RequestParameter> QueryParameters { get; set; } = [];
    public List<RequestParameter> HeaderParameters { get; set; } = [];
    public List<RequestParameter> BodyParameters { get; set; } = [];
    public string Subprotocol { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool FollowRedirects
    {
        get; set;
    }
    public string? SourceId
    {
        get; set;
    }
    public string? SourceKey
    {
        get; set;
    }
    public string? OperationId
    {
        get; set;
    }
    public string? ImportBaselineJson
    {
        get; set;
    }
    public bool SourceRemoved
    {
        get; set;
    }
}

public sealed class RequestParameter
{
    public string Description { get; set; } = "";
    public string Type { get; set; } = "Text";
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class HttpResult
{
    public int StatusCode
    {
        get; set;
    }
    public string StatusText { get; set; } = "";
    public string Headers { get; set; } = "";
    public string Body { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string? FileName { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DownloadPath { get; set; }
    public long DurationMs
    {
        get; set;
    }
    public long Size
    {
        get; set;
    }
    public string? Error
    {
        get; set;
    }
    public bool Truncated
    {
        get; set;
    }
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string EnvironmentName { get; set; } = "公共变量";
    public string FolderName { get; set; } = "工作区";
    public RequestDefinition Request { get; set; } = new();
    public HttpResult Response { get; set; } = new();
    public List<SocketMessage> Messages { get; set; } = [];
    public string Summary => $"{Request.Method}  {Request.Name}  ·  {(Response.Error is null ? Response.StatusCode.ToString() : "错误")}  ·  {Timestamp:MM-dd HH:mm:ss}";
}

public sealed class SocketMessage
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Direction { get; set; } = "接收";
    public string Kind { get; set; } = "Text";
    public string Content { get; set; } = "";
    public string Display => $"{Timestamp:HH:mm:ss.fff}  {Direction}  [{Kind}]  {Content}";
}

public sealed class CaptureRecord
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public string RequestHeaders { get; set; } = "";
    public string RequestBody { get; set; } = "";
    public HttpResult Response { get; set; } = new();
    public string Summary => $"{Timestamp:HH:mm:ss}  {Method}  {Response.StatusCode}  {Url}";
}
