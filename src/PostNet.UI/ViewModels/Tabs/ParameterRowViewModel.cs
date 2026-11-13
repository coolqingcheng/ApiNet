using PostNet.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels.Tabs;

public partial class ParameterRowViewModel : ViewModelBase
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _key;
    [ObservableProperty] private string _value;
    [ObservableProperty] private string _description;
    [ObservableProperty] private bool _isDraft;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsFile))] private string _type;
    private readonly Func<Task<string?>>? _pickFile;
    public static string[] TypeOptions { get; } = ["Text", "File"];
    public bool IsFile => Type == "File";
    public IRelayCommand RemoveCommand { get; }
    public ParameterRowViewModel(RequestParameter parameter, Action<ParameterRowViewModel> remove, Func<Task<string?>>? pickFile = null)
    {
        _enabled = parameter.Enabled;
        _key = parameter.Key;
        _value = parameter.Value;
        _description = parameter.Description;
        _type = parameter.Type;
        _pickFile = pickFile;
        RemoveCommand = new RelayCommand(() => remove(this));
    }
    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        if (_pickFile is not null && await _pickFile() is { } path) { Value = path; Type = "File"; Enabled = true; }
    }
    public RequestParameter Snapshot() => new() { Enabled = Enabled, Key = Key, Value = Value, Type = Type, Description = Description };
}
