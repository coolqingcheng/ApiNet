using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PostNet.Core.Models;
using PostNet.Core.Interfaces;
using PostNet.Desktop.ViewModels.Dialogs;
using PostNet.Desktop.ViewModels.Settings;
using PostNet.Desktop.Views.Dialogs;
using PostNet.Desktop.Views.Settings;
namespace PostNet.Desktop.Services;

public sealed class DialogService(Func<Window> owner) : IDialogService
{
    public Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => new ImportPickerWindow(new ImportPickerViewModel(sources)).ShowDialog<ImportSelection?>(owner());
    public async Task<string?> PromptAsync(string title, string initial = "")
    {
        var model = new DialogViewModel(title, "", ["确定", "取消"], initial);
        var result = await new DialogWindow(model).ShowDialog<int>(owner());
        return result == 0 ? model.Value : null;
    }
    public async Task<bool> ConfirmAsync(string title, string message) => await ChooseAsync(title, message, ["确定", "取消"]) == 0;
    public Task<int> ChooseAsync(string title, string message, string[] choices) => new DialogWindow(new DialogViewModel(title, message, choices)).ShowDialog<int>(owner());
    public async Task<string?> OpenJsonAsync()
    {
        var files = await owner().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入 OpenAPI / Postman / ApiPost JSON", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("请求集合 / 环境 JSON") { Patterns = ["*.json"] }] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task<string?> SaveJsonAsync(string name)
    {
        var file = await owner().StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出 OpenAPI", SuggestedFileName = name, DefaultExtension = "json", ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
        return file?.TryGetLocalPath();
    }
    public async Task<string?> WriteDocumentAsync(string name, byte[] content)
    {
        var extension = Path.GetExtension(name).TrimStart('.');
        var label = extension switch { "md" => "Markdown 文档", "pdf" => "PDF 文档", _ => "OpenAPI JSON" };
        using var file = await owner().StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出 " + label, SuggestedFileName = name, DefaultExtension = extension, ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType(label) { Patterns = ["*." + extension] }] });
        if (file is null) return null;
        await using var output = await file.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await output.WriteAsync(content);
        return file.TryGetLocalPath() ?? file.Name;
    }
    public async Task<string?> OpenUploadFileAsync()
    {
        var files = await owner().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择要上传的文件", AllowMultiple = false });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task EditEnvironmentsAsync(WorkspaceDocument workspace) => await new EnvironmentWindow(new EnvironmentEditorViewModel(workspace)).ShowDialog(owner());
    public async Task<string?> SaveResponseAsync(string name)
    {
        var file = await owner().StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存响应文件到本地", SuggestedFileName = name, ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }
    public async Task EditSettingsAsync(WorkspaceDocument workspace) => await new SettingsWindow(new SettingsViewModel(workspace.Settings)).ShowDialog(owner());
}
