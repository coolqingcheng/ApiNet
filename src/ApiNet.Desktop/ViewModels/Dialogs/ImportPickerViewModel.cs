using ApiNet.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels.Dialogs;

public sealed record ImportSelection(LocalImportSource? Source);

public partial class ImportPickerViewModel : ViewModelBase
{
    public IReadOnlyList<LocalImportSource> Sources { get; }
    public bool HasSources => Sources.Count > 0;
    public bool NoSources => !HasSources;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ImportLocalCommand))] private LocalImportSource? _selectedSource;
    public event Action<ImportSelection?>? Completed;
    public ImportPickerViewModel(IReadOnlyList<LocalImportSource> sources) { Sources = sources; _selectedSource = sources.FirstOrDefault(); }
    private bool CanImportLocal() => SelectedSource is not null;
    [RelayCommand(CanExecute = nameof(CanImportLocal))] private void ImportLocal() => Completed?.Invoke(new(SelectedSource));
    [RelayCommand] private void ChooseFile() => Completed?.Invoke(new(null));
    [RelayCommand] private void Cancel() => Completed?.Invoke(null);
}
