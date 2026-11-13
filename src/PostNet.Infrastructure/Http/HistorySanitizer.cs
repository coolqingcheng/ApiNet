using PostNet.Core.Models;
using PostNet.Core.Serialization;

namespace PostNet.Infrastructure.Http;

public static class HistorySanitizer
{
    public static WorkspaceDocument SnapshotContext(WorkspaceDocument workspace) => new()
    {
        SelectedEnvironmentId = workspace.SelectedEnvironmentId,
        SharedVariables = workspace.SharedVariables.Select(x => new VariableDefinition { Key = x.Key, Value = x.Value, IsSecret = x.IsSecret }).ToList(),
        Environments = workspace.Environments.Where(x => x.Id == workspace.SelectedEnvironmentId).Select(x => new EnvironmentDefinition { Id = x.Id, Name = x.Name, Variables = x.Variables.Select(v => new VariableDefinition { Key = v.Key, Value = v.Value, IsSecret = v.IsSecret }).ToList() }).ToList(),
        Folders = workspace.Folders.Select(x => new FolderDefinition { Id = x.Id, Name = x.Name, ParentId = x.ParentId }).ToList()
    };
    public static string Redact(string value, WorkspaceDocument workspace)
    {
        foreach (var variable in workspace.SharedVariables.Concat(workspace.Environments.SelectMany(x => x.Variables)).Where(x => x.IsSecret && x.Value.Length > 0).OrderByDescending(x => x.Value.Length))
            value = value.Replace(variable.Value, "[已脱敏]", StringComparison.Ordinal);
        return value;
    }
    public static HistoryEntry Create(RequestDefinition resolved, HttpResult response, WorkspaceDocument workspace)
    {
        var secrets = workspace.SharedVariables.Concat(workspace.Environments.FirstOrDefault(x => x.Id == workspace.SelectedEnvironmentId)?.Variables ?? [])
            .Where(x => x.IsSecret && !string.IsNullOrEmpty(x.Value)).Select(x => x.Value).OrderByDescending(x => x.Length).ToArray();
        string Redact(string value)
        {
            foreach (var secret in secrets)
                value = value.Replace(secret, "[已脱敏]", StringComparison.Ordinal);
            return value;
        }
        static string Headers(string value) => string.Join('\n', value.Split('\n').Select(line =>
        {
            var index = line.IndexOf(':');
            return index > 0 && new[] { "authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key" }.Contains(line[..index].Trim().ToLowerInvariant()) ? line[..index] + ": [已脱敏]" : line;
        }));
        var request = ModelCopy.Request(resolved);
        request.Url = Redact(request.Url);
        request.Headers = Redact(Headers(request.Headers));
        request.Body = Redact(request.Body);
        foreach (var parameter in request.BodyParameters)
        {
            parameter.Key = Redact(parameter.Key);
            parameter.Value = Redact(parameter.Value);
        }
        request.ImportBaselineJson = null;
        return new HistoryEntry
        {
            Request = request,
            EnvironmentName = workspace.Environments.FirstOrDefault(x => x.Id == workspace.SelectedEnvironmentId)?.Name ?? "公共变量",
            FolderName = workspace.Folders.FirstOrDefault(x => x.Id == request.FolderId)?.Name ?? "工作区",
            Response = new HttpResult
            {
                StatusCode = response.StatusCode,
                StatusText = response.StatusText,
                DurationMs = response.DurationMs,
                Size = response.Size,
                Error = response.Error is null ? null : Redact(response.Error),
                Body = Redact(response.Body),
                Headers = Redact(Headers(response.Headers)),
                Truncated = response.Truncated,
                ContentType = response.ContentType,
                FileName = response.FileName
            }
        };
    }
}
