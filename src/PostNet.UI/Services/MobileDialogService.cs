using System.Security.Cryptography;
using System.Text;
using PostNet.Core.Interfaces;
using PostNet.Core.Models;
using PostNet.Desktop.ViewModels.Dialogs;
using PostNet.Desktop.ViewModels.Settings;
using PostNet.Desktop.Views.Mobile;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PostNet.Desktop.Services;

public sealed class MobileDialogService(Func<MobileMainView> owner, string dataDirectory) : IDialogService
{
    private IStorageProvider Storage => TopLevel.GetTopLevel(owner())?.StorageProvider ?? throw new InvalidOperationException("文件选择器尚未就绪。");
    public async Task<int> ChooseAsync(string title, string message, string[] choices)
    {
        var model = new DialogViewModel(title, message, choices);
        var result = new TaskCompletionSource<int>(); model.Completed += choice => result.TrySetResult(choice);
        return await owner().DialogHost.ShowAsync(title, new MobilePromptView { DataContext = model }, result, -1);
    }
    public async Task<string?> PromptAsync(string title, string initial = "")
    {
        var model = new DialogViewModel(title, "", ["确定", "取消"], initial);
        var result = new TaskCompletionSource<int>(); model.Completed += choice => result.TrySetResult(choice);
        return await owner().DialogHost.ShowAsync(title, new MobilePromptView { DataContext = model }, result, -1) == 0 ? model.Value : null;
    }
    public async Task<bool> ConfirmAsync(string title, string message) => await ChooseAsync(title, message, ["确定", "取消"]) == 0;
    public async Task<ImportSelection?> PickImportAsync(IReadOnlyList<LocalImportSource> sources) => await ChooseAsync("导入到选中文件夹", "选择原软件导出的 JSON 文件，或 Swagger / OpenAPI JSON 在线链接。", ["选择 JSON 文件", "在线 JSON 链接", "取消"]) switch { 0 => new(null), 1 => new(null, true), _ => null };
    public Task<string?> OpenJsonAsync() => PickLocalCopyAsync(false);
    public Task<string?> OpenUploadFileAsync() => PickLocalCopyAsync(true);
    private async Task<string?> PickLocalCopyAsync(bool upload)
    {
        var files = await Storage.OpenFilePickerAsync(new() { Title = upload ? "选择上传文件" : "导入 JSON", AllowMultiple = false, FileTypeFilter = upload ? null : [new("JSON") { Patterns = ["*.json"], MimeTypes = ["application/json", "text/json", "text/plain"] }] });
        if (files.FirstOrDefault() is not { } file) return null;
        using (file)
        {
            // Android document URIs are accessed through streams, not filesystem paths.
            var id = upload ? Guid.NewGuid().ToString("N") : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Path.AbsoluteUri)));
            var directory = Path.Combine(dataDirectory, upload ? "uploads" : "imports", id); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Infrastructure.Http.HttpRequestService.SafeFileName(file.Name));
            await using var input = await file.OpenReadAsync();
            await using var output = File.Create(path); await input.CopyToAsync(output);
            return path;
        }
    }
    public Task<string?> SaveJsonAsync(string name) => Task.FromResult<string?>(null);
    public Task<string?> SaveResponseAsync(string name) => Task.FromResult<string?>(null);
    public async Task<string?> WriteJsonAsync(string name, string json)
    {
        using var file = await Storage.SaveFilePickerAsync(new() { Title = "导出 OpenAPI", SuggestedFileName = name, DefaultExtension = "json" });
        if (file is null) return null;
        await using var output = await file.OpenWriteAsync(); if (output.CanSeek) output.SetLength(0);
        await output.WriteAsync(Encoding.UTF8.GetBytes(json)); return file.Name;
    }
    public async Task<string?> SaveFileAsync(string source, string name)
    {
        using var file = await Storage.SaveFilePickerAsync(new() { Title = "保存文件", SuggestedFileName = name });
        if (file is null) return null;
        await using var input = File.OpenRead(source);
        await using var output = await file.OpenWriteAsync(); if (output.CanSeek) output.SetLength(0);
        await input.CopyToAsync(output); return file.Name;
    }
    public async Task EditEnvironmentsAsync(WorkspaceDocument workspace)
    {
        var model = new EnvironmentEditorViewModel(workspace); var result = new TaskCompletionSource<bool>(); model.Saved += () => result.TrySetResult(true);
        await owner().DialogHost.ShowAsync("环境变量", new MobileEnvironmentView { DataContext = model }, result, false);
    }
    public async Task EditSettingsAsync(WorkspaceDocument workspace)
    {
        var model = new SettingsViewModel(workspace.Settings); var result = new TaskCompletionSource<bool>(); model.Saved += () => result.TrySetResult(true);
        await owner().DialogHost.ShowAsync("设置", new MobileSettingsView { DataContext = model }, result, false);
    }
}
