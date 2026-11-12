using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiNet.Desktop.ViewModels.Tabs;

public sealed record ResponseHeaderRow(string Key, string Value);

public partial class ResponseHeadersViewModel : ViewModelBase
{
    [ObservableProperty] private string _rawText = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsTableView))] private bool _isTextView;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CountLabel), nameof(HasHeaders))]
    private IReadOnlyList<ResponseHeaderRow> _rows = [];

    public bool IsTableView { get => !IsTextView; set => IsTextView = !value; }
    public bool HasHeaders => Rows.Count > 0;
    public string CountLabel => $"{Rows.Count} 个响应头";

    partial void OnRawTextChanged(string value)
    {
        var rows = new List<ResponseHeaderRow>();
        foreach (var line in value.ReplaceLineEndings("\n").Split('\n'))
        {
            // Split once: URLs, dates and other header values can contain colons.
            var colon = line.IndexOf(':');
            if (colon > 0)
                rows.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
            else if (!string.IsNullOrWhiteSpace(line))
                rows.Add(new("", line.Trim()));
        }
        // Keep order and duplicate fields (notably Set-Cookie) intact.
        Rows = rows;
    }
}
