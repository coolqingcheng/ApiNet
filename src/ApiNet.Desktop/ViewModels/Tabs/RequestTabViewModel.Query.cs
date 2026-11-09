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
            var retained = QueryRows.Select((row, index) => (row, index)).Where(x => !x.row.Enabled || string.IsNullOrWhiteSpace(x.row.Key)).ToArray();
            foreach (var row in QueryRows.ToArray()) QueryRows.Remove(row);
            foreach (var parameter in QueryUrl.Parse(value)) AddRow(QueryRows, parameter);
            foreach (var (row, index) in retained) QueryRows.Insert(Math.Min(index, QueryRows.Count), row);
        }
        finally { _syncingRows = false; }
    }
}
