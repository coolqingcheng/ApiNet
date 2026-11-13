using System.Collections.ObjectModel;
using PostNet.Core.Interfaces;
using PostNet.Core.Models;
using PostNet.Core.Serialization;
using PostNet.Desktop.Services;
using PostNet.Desktop.ViewModels.Tabs;
using PostNet.Desktop.ViewModels.Workspace;
using PostNet.Infrastructure.WebSockets;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostNet.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private void FilterCaptures()
    {
        Captures.Clear();
        foreach (var record in _allCaptures.Where(x => CaptureSearch.Length == 0 || (x.Method + " " + x.Url + " " + x.Response.StatusCode).Contains(CaptureSearch, StringComparison.OrdinalIgnoreCase)))
            Captures.Add(record);
    }
    [RelayCommand]
    private void ClearCaptures()
    {
        _allCaptures.Clear();
        FilterCaptures();
    }
    [RelayCommand]
    private async Task ToggleProxyAsync()
    {
        try
        {
            if (_proxy.IsRunning)
                await _proxy.StopAsync();
            else
                await _proxy.StartAsync(Workspace.Settings.ProxyPort, Workspace.Settings.DecryptHttps);
            ProxyRunning = _proxy.IsRunning;
            Status = ProxyText;
        }
        catch (Exception exception) { Status = "代理启动失败：" + exception.Message; }
    }
    [RelayCommand]
    private async Task ExportCertificateAsync()
    {
        try
        {
            var path = _proxy.ExportCertificate();
            if (IsMobile)
            {
                var saved = await _dialogs.SaveFileAsync(path, Path.GetFileName(path));
                Status = saved is null ? "已取消证书导出" : "证书已保存：" + saved;
                return;
            }
            await _dialogs.ChooseAsync("HTTPS 解密证书", "证书已导出到：\n" + path + "\n\n请在需要抓包的客户端中信任此证书，并启用设置中的 HTTPS 解密。", ["知道了"]);
        }
        catch (Exception exception) { Status = exception.Message; }
    }
    [RelayCommand]
    private void ReplayCapture()
    {
        if (SelectedCapture is not { } capture)
            return;
        var request = new RequestDefinition { Name = "抓包 · " + capture.Url, Url = capture.Url, Method = capture.Method == "CONNECT" ? "GET" : capture.Method, Headers = capture.RequestHeaders, Body = capture.RequestBody, FolderId = TargetFolder };
        if (capture.Method.StartsWith("WS"))
        {
            request.Protocol = "WebSocket";
            request.Method = "WS";
            request.Url = request.Url.Replace("https://", "wss://").Replace("http://", "ws://");
        }
        OpenRequest(request);
        SelectedTab!.ShowHistory(capture.Response);
        SelectedTab.IsDirty = true;
    }
}
