using ApiNet.Core.Models;
using ApiNet.Core.Serialization;

namespace ApiNet.Desktop.ViewModels.Tabs;

public abstract partial class RequestTabViewModel
{
    private static RequestDefinition NormalizeQuery(RequestDefinition request)
    {
        var copy = ModelCopy.Request(request);
        QueryUrl.Normalize(copy);
        return copy;
    }

    partial void OnUrlChanged(string value)
    {
        if (_syncingRows) return;
        _syncingRows = true;
        try
        {
            var descriptions = QueryRows.Where(x => x.Enabled).GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => new Queue<string>(x.Select(row => row.Description)), StringComparer.Ordinal);
            var retained = QueryRows.Select((row, index) => (row, index)).Where(x => !x.row.Enabled || string.IsNullOrWhiteSpace(x.row.Key)).ToArray();
            foreach (var row in QueryRows.ToArray()) QueryRows.Remove(row);
            foreach (var parameter in QueryUrl.Parse(value))
            {
                if (descriptions.TryGetValue(parameter.Key, out var values) && values.Count > 0) parameter.Description = values.Dequeue();
                AddRow(QueryRows, parameter);
            }
            foreach (var (row, index) in retained) QueryRows.Insert(Math.Min(index, QueryRows.Count), row);
        }
        finally { _syncingRows = false; }
    }
}
