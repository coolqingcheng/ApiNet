using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace PostNet.Desktop.ViewModels.Dialogs;

public partial class DialogViewModel : ViewModelBase
{
    public string Title
    {
        get;
    }
    public string Message
    {
        get;
    }
    public bool ShowInput
    {
        get;
    }
    [ObservableProperty] private string _value;
    public ObservableCollection<DialogChoice> Choices { get; } = [];
    public event Action<int>? Completed;
    public DialogViewModel(string title, string message, string[] choices, string? input = null)
    {
        Title = title;
        Message = message;
        ShowInput = input is not null;
        _value = input ?? "";
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            Choices.Add(new DialogChoice(choices[i], new RelayCommand(() => Completed?.Invoke(index))));
        }
    }
}
public sealed record DialogChoice(string Label, IRelayCommand Command);
