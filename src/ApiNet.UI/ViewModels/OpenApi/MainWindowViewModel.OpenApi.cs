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
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(RefreshImportCommand))] private bool _isImporting;
    private bool CanImport() => !IsImporting;
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var folder = TargetFolder;
        IsImporting = true;
        try
        {
            Status = "正在检测本机 ApiPost / Postman…";
            var selection = await _dialogs.PickImportAsync(await _imports.DetectLocalAsync());
            if (selection is null) { Status = "已取消导入"; return; }
            if (selection.Online)
            {
                var url = await _dialogs.PromptAsync("在线 Swagger / OpenAPI JSON 链接");
                if (string.IsNullOrWhiteSpace(url)) { Status = "已取消导入"; return; }
                Status = "正在下载 Swagger / OpenAPI 文档…";
                var json = await _imports.ReadUrlAsync(url.Trim());
                await ImportDocumentAsync(json, folder, new Uri(url.Trim()).AbsoluteUri);
            }
            else if (selection.Source is { } local)
            {
                Status = "正在读取 " + local.Name + " 本地数据副本…";
                await ImportDocumentAsync(await _imports.ReadLocalAsync(local), folder, local.Location);
            }
            else if (await _dialogs.OpenJsonAsync() is { } path)
                await ImportDocumentAsync(await File.ReadAllTextAsync(path), folder, path);
            else Status = "已取消导入";
        }
        catch (Exception exception) { Status = "导入失败：" + exception.Message; await _dialogs.ChooseAsync("导入未完成", exception.Message + "\n\n可再次选择导入，并选择原软件导出的 JSON 文件。", ["返回"]); }
        finally { IsImporting = false; }
    }
    private async Task ImportDocumentAsync(string json, string? folder, string location)
    {
        var plan = _imports.Preview(Workspace, json, folder, location);
        var detail = plan.Summary + $"\n新文件夹 {plan.NewFolders.Count} · 公共变量 {plan.SharedVariables.Count} · 环境 {plan.Environments.Count}\n\n" + string.Join('\n', plan.Changes.Take(12).Select(x => x.Display + (x.ConflictFields.Count > 0 ? " [" + string.Join(", ", x.ConflictFields) + "]" : "")));
        if (plan.Warnings.Count > 0) detail += "\n\n需要检查：\n" + string.Join('\n', plan.Warnings.Select(x => "• " + x));
        var choice = await _dialogs.ChooseAsync(plan.Format + " 导入预览", detail, ["导入，保留本地冲突", "导入，采用文档冲突值", "取消"]);
        if (choice is < 0 or 2)
            { Status = "已取消导入"; return; }
        _imports.Apply(Workspace, plan, choice == 1);
        foreach (var tab in Tabs)
            if (Workspace.Requests.FirstOrDefault(x => x.Id == tab.Id) is { } updated)
                tab.ApplyImportedDefinition(updated);
        RefreshTree();
        RefreshEnvironments();
        await PersistSafelyAsync();
        Status = plan.Summary;
    }
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task RefreshImportAsync()
    {
        var sources = Workspace.ImportSources.Where(x => x.FolderId == TargetFolder).ToList();
        if (sources.Count == 0)
        {
            Status = "此文件夹尚未绑定导入源，请先导入 JSON。";
            return;
        }
        IsImporting = true;
        try
        {
            var index = sources.Count == 1 ? 0 : await _dialogs.ChooseAsync("选择要刷新的导入源", "此文件夹绑定了多个导入源。", sources.Select(x => x.Format + " · " + (x.Location.StartsWith("local://") ? "本机数据" : Path.GetFileName(x.Location))).Append("取消").ToArray());
            if (index < 0 || index >= sources.Count) return;
            var source = sources[index];
            string json;
            if (source.Location.StartsWith("local://", StringComparison.Ordinal))
            {
                var local = (await _imports.DetectLocalAsync()).FirstOrDefault(x => x.Location == source.Location) ?? throw new InvalidOperationException("未找到原软件的本地数据目录，请重新选择导入源。");
                Status = "正在读取本机数据副本…";
                json = await _imports.ReadLocalAsync(local);
            }
            else if (Uri.TryCreate(source.Location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                Status = "正在下载 Swagger / OpenAPI 文档…";
                json = await _imports.ReadUrlAsync(source.Location);
            }
            else json = await File.ReadAllTextAsync(source.Location);
            await ImportDocumentAsync(json, source.FolderId, source.Location);
        }
        catch (Exception exception) { Status = "刷新失败：" + exception.Message; }
        finally { IsImporting = false; }
    }

}
