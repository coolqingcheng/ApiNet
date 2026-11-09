using ApiNet.Core.Models;
using ApiNet.Core.Serialization;

namespace ApiNet.Infrastructure.Documents;

public static class RequestExportSanitizer
{
    private static readonly HashSet<string> PrivateHeaders = new(StringComparer.OrdinalIgnoreCase) { "authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key" };
    public static bool IsPrivateHeader(string key) => PrivateHeaders.Contains(key.Trim());
    public static string Text(string value, WorkspaceDocument workspace)
    {
        foreach (var secret in workspace.SharedVariables.Concat(workspace.Environments.SelectMany(x => x.Variables)).Where(x => x.IsSecret && x.Value.Length > 0).OrderByDescending(x => x.Value.Length))
            value = value.Replace(secret.Value, "{{" + secret.Key + "}}", StringComparison.Ordinal);
        return value;
    }
    public static RequestDefinition Clean(RequestDefinition request, WorkspaceDocument workspace)
    {
        var clean = ModelCopy.Request(request);
        clean.ImportBaselineJson = null;
        clean.Headers = string.Join('\n', clean.Headers.Split('\n').Where(line => { var separator = line.IndexOf(':'); return separator < 0 || !IsPrivateHeader(line[..separator]); }));
        clean.HeaderParameters.RemoveAll(x => IsPrivateHeader(x.Key));
        clean.Name = Text(clean.Name, workspace); clean.Url = Text(clean.Url, workspace);
        clean.Body = Text(clean.Body, workspace); clean.Headers = Text(clean.Headers, workspace); clean.Subprotocol = Text(clean.Subprotocol, workspace);
        foreach (var parameter in clean.QueryParameters.Concat(clean.HeaderParameters).Concat(clean.BodyParameters))
        {
            parameter.Key = Text(parameter.Key, workspace);
            parameter.Description = Text(parameter.Description, workspace);
            parameter.Value = parameter.Type == "File" ? "" : Text(parameter.Value, workspace);
            if (parameter.Type == "File") parameter.Enabled = false;
        }
        return clean;
    }
}
