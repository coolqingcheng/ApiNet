using System.Collections.ObjectModel;
using PostNet.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace PostNet.Desktop.ViewModels.Settings;

public partial class VariableRowViewModel : ViewModelBase
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PasswordCharacter))] private bool _isSecret;
    public char PasswordCharacter => IsSecret ? '●' : '\0';
}
public partial class EnvironmentEditorViewModel : ViewModelBase
{
    private readonly WorkspaceDocument _workspace;
    private EnvironmentDefinition? _editing;
    public ObservableCollection<EnvironmentDefinition> Environments { get; } = [];
    public ObservableCollection<VariableRowViewModel> Rows { get; } = [];
    [ObservableProperty] private EnvironmentDefinition? _selectedEnvironment;
    [ObservableProperty] private VariableRowViewModel? _selectedRow;
    [ObservableProperty] private string _environmentName = "";
    [ObservableProperty] private string _error = "";
    public event Action? Saved;
    public EnvironmentEditorViewModel(WorkspaceDocument workspace)
    {
        _workspace = workspace;
        Environments.Add(new EnvironmentDefinition { Id = "", Name = "公共变量", Variables = workspace.SharedVariables.Select(Copy).ToList() });
        foreach (var environment in workspace.Environments)
            Environments.Add(new EnvironmentDefinition { Id = environment.Id, Name = environment.Name, Variables = environment.Variables.Select(Copy).ToList() });
        SelectedEnvironment = Environments.FirstOrDefault(x => x.Id == workspace.SelectedEnvironmentId) ?? Environments[0];
    }
    private static VariableDefinition Copy(VariableDefinition variable) => new() { Key = variable.Key, Value = variable.Value, IsSecret = variable.IsSecret };
    partial void OnSelectedEnvironmentChanged(EnvironmentDefinition? value)
    {
        SaveCurrent();
        _editing = value;
        EnvironmentName = value?.Name ?? "";
        Rows.Clear();
        if (value is not null)
            foreach (var variable in value.Variables)
                Rows.Add(new VariableRowViewModel { Key = variable.Key, Value = variable.Value, IsSecret = variable.IsSecret });
    }
    private void SaveCurrent()
    {
        if (_editing is null)
            return;
        _editing.Name = EnvironmentName;
        _editing.Variables = Rows.Where(x => !string.IsNullOrWhiteSpace(x.Key)).Select(x => new VariableDefinition { Key = x.Key.Trim(), Value = x.Value, IsSecret = x.IsSecret }).ToList();
    }
    [RelayCommand]
    private void AddVariable()
    {
        var row = new VariableRowViewModel();
        Rows.Add(row);
        SelectedRow = row;
    }
    [RelayCommand]
    private void RemoveVariable()
    {
        if (SelectedRow is not null)
            Rows.Remove(SelectedRow);
    }
    [RelayCommand]
    private void AddEnvironment()
    {
        var environment = new EnvironmentDefinition();
        Environments.Add(environment);
        SelectedEnvironment = environment;
    }
    [RelayCommand]
    private void DuplicateEnvironment()
    {
        SaveCurrent();
        if (_editing is null)
            return;
        var environment = new EnvironmentDefinition { Name = EnvironmentName + " 副本", Variables = _editing.Variables.Select(Copy).ToList() };
        Environments.Add(environment);
        SelectedEnvironment = environment;
    }
    [RelayCommand]
    private void RemoveEnvironment()
    {
        if (SelectedEnvironment is not { Id.Length: > 0 } selected)
            return;
        SelectedEnvironment = Environments[0];
        Environments.Remove(selected);
    }
    [RelayCommand]
    private void Save()
    {
        SaveCurrent();
        if (Environments.Any(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            Error = "环境名称不能为空。";
            return;
        }
        if (Environments.Any(x => x.Variables.GroupBy(v => v.Key).Any(g => g.Count() > 1)))
        {
            Error = "同一环境中变量名不能重复。";
            return;
        }
        _workspace.SharedVariables = Environments[0].Variables;
        _workspace.Environments = Environments.Skip(1).ToList();
        if (_workspace.Environments.All(x => x.Id != _workspace.SelectedEnvironmentId))
            _workspace.SelectedEnvironmentId = null;
        Saved?.Invoke();
    }
}
