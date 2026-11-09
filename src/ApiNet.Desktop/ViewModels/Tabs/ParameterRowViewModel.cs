using ApiNet.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels.Tabs;

public partial class ParameterRowViewModel : ViewModelBase
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _key;
    [ObservableProperty] private string _value;
    public IRelayCommand RemoveCommand { get; }
    public ParameterRowViewModel(RequestParameter parameter, Action<ParameterRowViewModel> remove)
    {
        _enabled = parameter.Enabled;
        _key = parameter.Key;
        _value = parameter.Value;
        RemoveCommand = new RelayCommand(() => remove(this));
    }
    public RequestParameter Snapshot() => new() { Enabled = Enabled, Key = Key, Value = Value };
}
