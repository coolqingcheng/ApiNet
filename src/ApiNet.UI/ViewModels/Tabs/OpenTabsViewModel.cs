using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiNet.Desktop.ViewModels.Tabs;

public partial class OpenTabsViewModel : ViewModelBase
{
    private readonly ObservableCollection<RequestTabViewModel> _tabs;
    private readonly Action<RequestTabViewModel> _activate;
    private readonly HashSet<RequestTabViewModel> _tracked = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Matches), nameof(IsEmpty))] private string _search = "";
    public int Count => _tabs.Count;
    public bool IsEmpty => Matches.Count == 0;
    public IReadOnlyList<RequestTabViewModel> Matches => string.IsNullOrWhiteSpace(Search) ? _tabs.ToArray() : _tabs
        .Select((tab, index) => (Tab: tab, Index: index, Score: MatchScore(tab, Search)))
        .Where(x => x.Score >= 0).OrderBy(x => x.Score).ThenBy(x => x.Index).Select(x => x.Tab).ToArray();

    public OpenTabsViewModel(ObservableCollection<RequestTabViewModel> tabs, Action<RequestTabViewModel> activate)
    {
        _tabs = tabs; _activate = activate;
        tabs.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }
    public void Activate(RequestTabViewModel tab) { if (_tabs.Contains(tab)) _activate(tab); }
    private void Refresh()
    {
        foreach (var tab in _tracked.Where(x => !_tabs.Contains(x)).ToArray()) { tab.PropertyChanged -= TabChanged; _tracked.Remove(tab); }
        foreach (var tab in _tabs) if (_tracked.Add(tab)) tab.PropertyChanged += TabChanged;
        OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(Matches)); OnPropertyChanged(nameof(IsEmpty));
    }
    private void TabChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RequestTabViewModel.Name) or nameof(RequestTabViewModel.Method) or nameof(RequestTabViewModel.Url))
        { OnPropertyChanged(nameof(Matches)); OnPropertyChanged(nameof(IsEmpty)); }
    }
    private static int MatchScore(RequestTabViewModel tab, string query)
    {
        var text = (tab.Name + " " + tab.Badge + " " + tab.Url).ToUpperInvariant();
        var score = 0;
        foreach (var token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var needle = token.ToUpperInvariant();
            var exact = text.IndexOf(needle, StringComparison.Ordinal);
            if (exact >= 0) { score += exact; continue; }
            var position = 0;
            foreach (var character in needle)
            {
                var next = text.IndexOf(character, position);
                if (next < 0) return -1;
                score += next - position; position = next + 1;
            }
            score += text.Length;
        }
        return score;
    }
}
