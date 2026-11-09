using ApiNet.Core.Models;

namespace ApiNet.Desktop.Services;

public interface IDialogService
{
    Task<string?> PromptAsync(string title, string initial = "");
    Task<bool> ConfirmAsync(string title, string message);
    Task<int> ChooseAsync(string title, string message, string[] choices);
    Task<string?> OpenJsonAsync();
    Task<string?> SaveJsonAsync(string name);
    Task<string?> SaveResponseAsync(string name);
    Task EditEnvironmentsAsync(WorkspaceDocument workspace);
    Task EditSettingsAsync(WorkspaceDocument workspace);
}
