using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using PostNet.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels.Tabs;

public partial class ParameterTableViewModel : ViewModelBase, IDisposable
{
    private ObservableCollection<ParameterRowViewModel>? _rows;
    private readonly HashSet<ParameterRowViewModel> _tracked = [];
    private readonly Func<Task<string?>> _pickFile;
    private bool _updating;
    private string _lastAppliedBulk = "";
    public ObservableCollection<ParameterRowViewModel> Items { get; } = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(EditorToggleLabel), nameof(HasPendingBulkChanges))] private bool _isBulkEditing;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasPendingBulkChanges))] private string _bulkText = "";
    [ObservableProperty] private string _bulkError = "";
    public string EditorToggleLabel => IsBulkEditing ? "表格编辑" : "批量编辑";
    public bool HasPendingBulkChanges => IsBulkEditing && BulkText != _lastAppliedBulk;
    public string CountLabel => _rows is null || _rows.Count == 0 ? "" : $"{_rows.Count(x => x.Enabled && x.Key.Length > 0)} / {_rows.Count(x => x.Key.Length > 0)}";
    public bool HasRows => _rows?.Count > 0;
    [RelayCommand] private void ToggleAll() => AllEnabled = AllEnabled != true;
    public bool? AllEnabled
    {
        get => _rows is not { Count: > 0 } ? false : _rows.All(x => x.Enabled) ? true : _rows.All(x => !x.Enabled) ? false : null;
        set
        {
            if (_rows is null) return;
            foreach (var row in _rows.ToArray()) row.Enabled = value ?? true;
            NotifyCounts();
        }
    }

    public ParameterTableViewModel(Func<Task<string?>> pickFile)
    {
        _pickFile = pickFile;
        Items.Add(NewDraft());
    }

    public void SetRows(ObservableCollection<ParameterRowViewModel>? rows)
    {
        if (ReferenceEquals(_rows, rows)) return;
        if (_rows is not null) _rows.CollectionChanged -= RowsChanged;
        foreach (var row in _tracked) row.PropertyChanged -= RowChanged;
        _tracked.Clear(); Items.Clear(); _rows = rows;
        if (_rows is not null)
        {
            _rows.CollectionChanged += RowsChanged;
            foreach (var row in _rows) { Track(row); Items.Add(row); }
        }
        Items.Add(NewDraft()); IsBulkEditing = false; BulkError = "";
        NotifyCounts();
    }

    private ParameterRowViewModel NewDraft()
    {
        var draft = new ParameterRowViewModel(new RequestParameter(), row => _rows?.Remove(row), _pickFile) { IsDraft = true };
        Track(draft);
        return draft;
    }
    private void Track(ParameterRowViewModel row) { if (_tracked.Add(row)) row.PropertyChanged += RowChanged; }
    private void RowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_updating || sender is not ParameterRowViewModel row) return;
        if (row.IsDraft && _rows is not null && (row.Key.Length > 0 || row.Value.Length > 0 || row.Description.Length > 0 || row.IsFile))
        {
            _updating = true;
            try { row.IsDraft = false; _rows.Add(row); Items.Add(NewDraft()); }
            finally { _updating = false; }
        }
        NotifyCounts();
    }
    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_updating) return;
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in Items.Where(x => !x.IsDraft).ToArray()) RemoveItem(row);
        }
        else
        {
            if (args.OldItems is not null) foreach (ParameterRowViewModel row in args.OldItems) RemoveItem(row);
            if (args.NewItems is not null)
            {
                var index = args.NewStartingIndex < 0 ? Items.Count - 1 : args.NewStartingIndex;
                foreach (ParameterRowViewModel row in args.NewItems) { Track(row); Items.Insert(Math.Min(index++, Items.Count - 1), row); }
            }
        }
        NotifyCounts();
    }
    private void RemoveItem(ParameterRowViewModel row) { Items.Remove(row); row.PropertyChanged -= RowChanged; _tracked.Remove(row); }
    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(AllEnabled)); OnPropertyChanged(nameof(HasRows)); OnPropertyChanged(nameof(CountLabel));
        if (IsBulkEditing && !HasPendingBulkChanges)
        {
            _lastAppliedBulk = FormatBulk();
            BulkText = _lastAppliedBulk;
        }
    }
    private string FormatBulk() => string.Join('\n', (_rows ?? []).Select(x => (x.Enabled ? "" : "// ") + x.Key + ": " + x.Value));

    [RelayCommand]
    private void ToggleBulk()
    {
        if (!IsBulkEditing)
        {
            BulkText = FormatBulk();
            _lastAppliedBulk = BulkText;
            BulkError = ""; IsBulkEditing = true;
            return;
        }
        if (TryApplyBulk()) IsBulkEditing = false;
    }
    public bool TryApplyBulk()
    {
        if (!IsBulkEditing || !HasPendingBulkChanges) return true;
        if (_rows is null) return false;
        var old = _rows.GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => new Queue<RequestParameter>(x.Select(row => row.Snapshot())), StringComparer.Ordinal);
        List<RequestParameter> parsed = [];
        var lines = BulkText.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0) continue;
            var enabled = !line.StartsWith("//", StringComparison.Ordinal);
            if (!enabled) line = line[2..].TrimStart();
            var separator = line.IndexOf(':');
            if (separator <= 0) { BulkError = $"第 {index + 1} 行需要使用 key: value 格式。"; return false; }
            var key = line[..separator].Trim();
            var parameter = old.TryGetValue(key, out var previous) && previous.Count > 0 ? previous.Dequeue() : new RequestParameter();
            parameter.Key = key; parameter.Value = line[(separator + 1)..].Trim(); parameter.Enabled = enabled;
            parsed.Add(parameter);
        }
        _rows.Clear();
        foreach (var parameter in parsed)
        {
            var row = new ParameterRowViewModel(parameter, item => _rows.Remove(item), _pickFile);
            _rows.Add(row);
        }
        BulkError = ""; _lastAppliedBulk = BulkText; OnPropertyChanged(nameof(HasPendingBulkChanges));
        return true;
    }
    [RelayCommand] private void CancelBulk() { IsBulkEditing = false; BulkError = ""; }
    public void Dispose()
    {
        if (_rows is not null) _rows.CollectionChanged -= RowsChanged;
        foreach (var row in _tracked) row.PropertyChanged -= RowChanged;
        _tracked.Clear();
    }
}
