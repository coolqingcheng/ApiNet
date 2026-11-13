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
    public async Task RecordHistoryAsync(HistoryEntry history)
    {
        await _store.AddHistoryAsync(history);
        var summary = new HistoryEntry
        {
            Id = history.Id,
            Timestamp = history.Timestamp,
            EnvironmentName = history.EnvironmentName,
            FolderName = history.FolderName,
            Request = new RequestDefinition { Id = history.Request.Id, Name = history.Request.Name, Method = history.Request.Method, Protocol = history.Request.Protocol, Url = history.Request.Url },
            Response = new HttpResult { StatusCode = history.Response.StatusCode, StatusText = history.Response.StatusText, Error = history.Response.Error, DurationMs = history.Response.DurationMs, Size = history.Response.Size }
        };
        _allHistory.RemoveAll(x => x.Id == history.Id);
        _allHistory.Insert(0, summary);
        if (_allHistory.Count > 1000)
            _allHistory.RemoveAt(_allHistory.Count - 1);
        FilterHistory();
    }
    private void FilterHistory()
    {
        var environments = _allHistory.Select(x => x.EnvironmentName).Distinct().ToArray();
        foreach (var environment in environments)
            if (!HistoryEnvironments.Contains(environment))
                HistoryEnvironments.Add(environment);
        var since = HistoryPeriod switch
        {
            "今天" => new DateTimeOffset(DateTime.Today),
            "最近 7 天" => DateTimeOffset.Now.AddDays(-7),
            "最近 30 天" => DateTimeOffset.Now.AddDays(-30),
            _ => DateTimeOffset.MinValue
        };
        History.Clear();
        foreach (var entry in _allHistory.Where(x => x.Timestamp >= since && (HistoryFrom is null || x.Timestamp >= HistoryFrom.Value.Date) && (HistoryTo is null || x.Timestamp < HistoryTo.Value.Date.AddDays(1))
            && (HistoryMethod == "全部方法" || x.Request.Method == HistoryMethod)
            && (HistoryEnvironment == "全部环境" || x.EnvironmentName == HistoryEnvironment)
            && (HistoryResult == "全部结果" || (HistoryResult == "网络错误" ? x.Response.Error is not null : HistoryResult == "HTTP 错误" ? x.Response.StatusCode >= 400 : x.Response.Error is null && x.Response.StatusCode is >= 100 and < 400))
            && (HistorySearch.Length == 0 || (x.Request.Name + " " + x.Request.Url + " " + x.FolderName + " " + x.Response.StatusCode).Contains(HistorySearch, StringComparison.OrdinalIgnoreCase))))
            History.Add(entry);
    }
    [RelayCommand]
    private async Task RestoreHistoryAsync()
    {
        if (SelectedHistory is not { } selected)
            return;
        var history = await _store.GetHistoryDetailAsync(selected.Id);
        if (history is null)
        {
            Status = "历史记录不存在。";
            return;
        }
        var request = ModelCopy.Request(history.Request);
        request.Id = Guid.NewGuid().ToString("N");
        request.Name += " · 历史";
        request.SourceId = null;
        request.ImportBaselineJson = null;
        OpenRequest(request);
        SelectedTab!.ShowHistory(history.Response);
        SelectedTab.IsDirty = true;
        if (SelectedTab is WebSocketTabViewModel socket)
            foreach (var message in history.Messages)
                socket.Messages.Add(message);
    }
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!await _dialogs.ConfirmAsync("清空历史", "确定删除所有请求历史？"))
            return;
        await _store.ClearHistoryAsync();
        _allHistory.Clear();
        FilterHistory();
    }
}
