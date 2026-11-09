using System.Collections.ObjectModel;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Core.Serialization;
using ApiNet.Desktop.Services;
using ApiNet.Desktop.ViewModels.Tabs;
using ApiNet.Desktop.ViewModels.Workspace;
using ApiNet.Infrastructure.WebSockets;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ImportAsync()
    {
        var path = await _dialogs.OpenJsonAsync();
        if (path is null)
            return;
        await ImportFileAsync(path, TargetFolder);
    }
    private async Task ImportFileAsync(string path, string? folder)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var plan = _openApi.Preview(Workspace, json, folder, path);
            var detail = plan.Summary + "\n\n" + string.Join('\n', plan.Changes.Take(12).Select(x => x.Display + (x.ConflictFields.Count > 0 ? " [" + string.Join(", ", x.ConflictFields) + "]" : "")));
            var choice = await _dialogs.ChooseAsync("OpenAPI 导入预览", detail, ["导入，保留本地冲突", "导入，采用文档冲突值", "取消"]);
            if (choice is < 0 or 2)
                return;
            _openApi.Apply(Workspace, plan, choice == 1);
            foreach (var tab in Tabs)
                if (Workspace.Requests.FirstOrDefault(x => x.Id == tab.Id) is { } updated)
                    tab.ApplyImportedDefinition(updated);
            RefreshTree();
            await PersistSafelyAsync();
            Status = plan.Summary;
        }
        catch (Exception exception) { Status = "导入失败：" + exception.Message; }
    }
    [RelayCommand]
    private async Task RefreshImportAsync()
    {
        var source = Workspace.ImportSources.FirstOrDefault(x => x.FolderId == TargetFolder);
        if (source is null)
        {
            Status = "此文件夹尚未绑定导入源，请先导入 JSON。";
            return;
        }
        await ImportFileAsync(source.Location, source.FolderId);
    }
    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            var json = _openApi.Export(Workspace, TargetFolder);
            var path = await _dialogs.SaveJsonAsync("openapi.json");
            if (path is not null)
            {
                await File.WriteAllTextAsync(path, json);
                Status = "OpenAPI 已导出";
            }
        }
        catch (Exception exception) { Status = "导出失败：" + exception.Message; }
    }

}
