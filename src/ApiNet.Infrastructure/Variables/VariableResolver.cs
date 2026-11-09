using System.Text.RegularExpressions;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Core.Serialization;

namespace ApiNet.Infrastructure.Variables;

public sealed partial class VariableResolver : IVariableResolver
{
    [GeneratedRegex(@"\{\{\s*([\w.\-]+)\s*\}\}")]
    private static partial Regex Placeholder();

    public string Resolve(string text, WorkspaceDocument workspace)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in workspace.SharedVariables)
            variables[variable.Key] = variable.Value;
        foreach (var variable in workspace.Environments.FirstOrDefault(x => x.Id == workspace.SelectedEnvironmentId)?.Variables ?? [])
            variables[variable.Key] = variable.Value;
        string Expand(string input, HashSet<string> stack) => Placeholder().Replace(input, match =>
        {
            var key = match.Groups[1].Value;
            if (!variables.TryGetValue(key, out var value))
                throw new InvalidOperationException($"变量 {{{{{key}}}}} 未配置，请检查当前环境。");
            if (!stack.Add(key))
                throw new InvalidOperationException($"变量 {key} 存在循环引用。");
            if (stack.Count > 32)
                throw new InvalidOperationException("变量引用层级超过限制。");
            var expanded = Expand(value, stack);
            stack.Remove(key);
            return expanded;
        });
        return Expand(text, []);
    }

    public RequestDefinition ResolveRequest(RequestDefinition request, WorkspaceDocument workspace)
    {
        var resolved = ModelCopy.Request(request);
        resolved.Url = Resolve(request.Url, workspace);
        var query = request.QueryParameters.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => Uri.EscapeDataString(Resolve(x.Key, workspace)) + "=" + Uri.EscapeDataString(Resolve(x.Value, workspace))).ToArray();
        if (query.Length > 0)
        {
            var hash = resolved.Url.IndexOf('#');
            var fragment = hash >= 0 ? resolved.Url[hash..] : "";
            var url = hash >= 0 ? resolved.Url[..hash] : resolved.Url;
            resolved.Url = url + (url.Contains('?') ? "&" : "?") + string.Join('&', query) + fragment;
        }
        resolved.QueryParameters.Clear();
        resolved.HeaderParameters.Clear();
        resolved.Headers = Resolve(request.Headers, workspace);
        resolved.Body = Resolve(request.Body, workspace);
        resolved.Subprotocol = Resolve(request.Subprotocol, workspace);
        return resolved;
    }
}
