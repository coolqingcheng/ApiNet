using ApiNet.Core.Models;
using ApiNet.Core.Interfaces;
using ApiNet.Desktop.ViewModels.Dialogs;

namespace ApiNet.Desktop.Services;

public interface IDialogService
{
    Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources);
    Task<string?> PromptAsync(string title, string initial = "");
    Task<bool> ConfirmAsync(string title, string message);
    Task<int> ChooseAsync(string title, string message, string[] choices);
    Task<string?> OpenJsonAsync();
    Task<string?> OpenUploadFileAsync();
    Task<string?> SaveJsonAsync(string name);
    Task<string?> SaveResponseAsync(string name);
    async Task<string?> WriteDocumentAsync(string name, byte[] content)
    {
        var path = await SaveResponseAsync(name);
        if (path is not null) await File.WriteAllBytesAsync(path, content);
        return path;
    }
    async Task<string?> WriteJsonAsync(string name, string json)
    {
        var path = await SaveJsonAsync(name);
        if (path is not null) await File.WriteAllTextAsync(path, json);
        return path;
    }
    async Task<string?> SaveFileAsync(string source, string name)
    {
        var path = await SaveResponseAsync(name);
        if (path is null) return null;
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await input.CopyToAsync(output);
        return path;
    }
    Task EditEnvironmentsAsync(WorkspaceDocument workspace);
    Task EditSettingsAsync(WorkspaceDocument workspace);
}
