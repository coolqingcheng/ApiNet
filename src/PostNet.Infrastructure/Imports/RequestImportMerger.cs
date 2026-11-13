using System.Text.Json;
using PostNet.Core.Models;
using PostNet.Core.Serialization;

namespace PostNet.Infrastructure.Imports;

internal static class RequestImportMerger
{
    private static readonly string[] Fields = ["Protocol", "FolderId", "TimeoutSeconds", "FollowRedirects", "BodyParameters", "Name", "Method", "Url", "Headers", "QueryParameters", "Body", "BodyFormat", "Subprotocol"];
    public static void Preview(WorkspaceDocument workspace, ImportPlan plan, List<RequestDefinition> incoming)
    {
        var source = plan.Source;
        var previous = workspace.Requests.Where(x => x.SourceId == source.Id).ToList();
        HashSet<string> matched = [];
        foreach (var request in incoming)
        {
            var existing = previous.FirstOrDefault(x => x.SourceKey == request.SourceKey);
            if (existing is null && request.OperationId is not null)
            {
                var matches = previous.Where(x => x.OperationId == request.OperationId && !matched.Contains(x.Id)).ToArray();
                if (matches.Length == 1 && incoming.Count(x => x.OperationId == request.OperationId) == 1)
                    existing = matches[0];
            }
            var change = new ImportChange { Incoming = request, ExistingId = existing?.Id, Kind = existing is null ? "新增" : "更新" };
            if (existing is not null)
            {
                matched.Add(existing.Id);
                var baseline = Baseline(existing);
                foreach (var field in Fields)
                    if (Value(existing, field) != Value(baseline, field) && Value(request, field) != Value(baseline, field) && Value(existing, field) != Value(request, field))
                        change.ConflictFields.Add(field);
                if (change.ConflictFields.Count > 0)
                    change.Kind = "冲突";
            }
            plan.Changes.Add(change);
        }
        foreach (var deleted in previous.Where(x => !matched.Contains(x.Id)))
            plan.Changes.Add(new ImportChange { Kind = "移除", ExistingId = deleted.Id, Incoming = ModelCopy.Request(deleted) });
    }
    public static void Apply(WorkspaceDocument workspace, ImportPlan plan, bool overwriteConflicts)
    {
        foreach (var folder in plan.NewFolders)
            if (workspace.Folders.All(x => x.Id != folder.Id))
                workspace.Folders.Add(folder);
        foreach (var change in plan.Changes)
        {
            var existing = workspace.Requests.FirstOrDefault(x => x.Id == change.ExistingId);
            if (change.Kind == "移除")
            {
                if (existing is not null)
                    existing.SourceRemoved = true;
                continue;
            }
            var incoming = ModelCopy.Request(change.Incoming);
            QueryUrl.Normalize(incoming);
            var baselineJson = JsonSerializer.Serialize(incoming, AppJsonContext.Default.RequestDefinition);
            if (existing is null)
            {
                incoming.ImportBaselineJson = baselineJson;
                workspace.Requests.Add(incoming);
                continue;
            }
            var baseline = Baseline(existing);
            QueryUrl.Normalize(baseline);
            QueryUrl.Normalize(existing);
            foreach (var field in Fields)
                if (Value(existing, field) == Value(baseline, field) || (overwriteConflicts && change.ConflictFields.Contains(field)))
                    SetValue(existing, field, Value(incoming, field));
            existing.SourceId = incoming.SourceId;
            existing.SourceKey = incoming.SourceKey;
            existing.OperationId = incoming.OperationId;
            existing.ImportBaselineJson = baselineJson;
            existing.SourceRemoved = false;
        }
    }
    private static RequestDefinition Baseline(RequestDefinition request) => request.ImportBaselineJson is null ? ModelCopy.Request(request) : JsonSerializer.Deserialize(request.ImportBaselineJson, AppJsonContext.Default.RequestDefinition)!;
    private static List<RequestParameter> HeaderValues(RequestDefinition request) => request.HeaderParameters.Count > 0 ? request.HeaderParameters : Http.HeaderParser.Parse(request.Headers).Select(x => new RequestParameter { Key = x.Key, Value = x.Value }).ToList();
    private static string Value(RequestDefinition request, string field) => field switch { "Protocol" => request.Protocol, "FolderId" => request.FolderId ?? "", "TimeoutSeconds" => request.TimeoutSeconds.ToString(), "FollowRedirects" => request.FollowRedirects.ToString(), "BodyParameters" => JsonSerializer.Serialize(request.BodyParameters, AppJsonContext.Default.ListRequestParameter), "Name" => request.Name, "Method" => request.Method, "Url" => QueryUrl.WithoutQuery(request.Url), "Headers" => JsonSerializer.Serialize(HeaderValues(request), AppJsonContext.Default.ListRequestParameter), "QueryParameters" => JsonSerializer.Serialize(QueryUrl.Parameters(request), AppJsonContext.Default.ListRequestParameter), "Body" => request.Body, "BodyFormat" => request.BodyFormat, "Subprotocol" => request.Subprotocol, _ => "" };
    private static void SetValue(RequestDefinition request, string field, string value)
    {
        switch (field)
        {
            case "Protocol": request.Protocol = value; break;
            case "FolderId": request.FolderId = value.Length == 0 ? null : value; break;
            case "TimeoutSeconds": request.TimeoutSeconds = int.Parse(value); break;
            case "FollowRedirects": request.FollowRedirects = bool.Parse(value); break;
            case "BodyParameters": request.BodyParameters = JsonSerializer.Deserialize(value, AppJsonContext.Default.ListRequestParameter) ?? []; break;
            case "Name":
                request.Name = value;
                break;
            case "Method":
                request.Method = value;
                break;
            case "Url":
                request.Url = value;
                break;
            case "Headers":
                request.HeaderParameters = JsonSerializer.Deserialize(value, AppJsonContext.Default.ListRequestParameter) ?? [];
                request.Headers = string.Join('\n', request.HeaderParameters.Where(x => x.Enabled && x.Key.Length > 0).Select(x => x.Key + ": " + x.Value));
                break;
            case "QueryParameters":
                request.QueryParameters = JsonSerializer.Deserialize(value, AppJsonContext.Default.ListRequestParameter) ?? [];
                break;
            case "BodyFormat":
                request.BodyFormat = value;
                break;
            case "Body":
                request.Body = value;
                break;
            case "Subprotocol":
                request.Subprotocol = value;
                break;
        }
    }
}
