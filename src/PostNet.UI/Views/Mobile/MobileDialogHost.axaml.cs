using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PostNet.Desktop.Views.Mobile;

public partial class MobileDialogHost : UserControl
{
    private readonly SemaphoreSlim _gate = new(1);
    private Action? _cancel;
    public MobileDialogHost() => InitializeComponent();
    public async Task<T> ShowAsync<T>(string title, Control content, TaskCompletionSource<T> completion, T cancelled)
    {
        await _gate.WaitAsync();
        try
        {
            DialogTitle.Text = title; DialogBody.Content = content; _cancel = () => completion.TrySetResult(cancelled); IsVisible = true;
            return await completion.Task;
        }
        finally { _cancel = null; IsVisible = false; DialogBody.Content = null; _gate.Release(); }
    }
    public bool Dismiss() { if (_cancel is null) return false; _cancel(); return true; }
    private void Cancel(object? sender, RoutedEventArgs args) => Dismiss();
}
