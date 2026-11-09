using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiNet.Core.Models;

namespace ApiNet.Desktop.ViewModels.Workspace;

public partial class WorkspaceNodeViewModel : ViewModelBase
{
    public string Id
    {
        get;
    }
    public bool IsFolder
    {
        get;
    }
    public string Name
    {
        get;
    }
    public string Badge
    {
        get;
    }
    public string BadgeColor
    {
        get;
    }
    public bool SourceRemoved
    {
        get;
    }
    public ObservableCollection<WorkspaceNodeViewModel> Children { get; } = [];
    [ObservableProperty] private bool _isExpanded = true;
    public WorkspaceNodeViewModel(FolderDefinition folder)
    {
        Id = folder.Id;
        IsFolder = true;
        Name = folder.Name;
        Badge = "▱";
        BadgeColor = "#B6B9C6";
    }
    public WorkspaceNodeViewModel(RequestDefinition request)
    {
        Id = request.Id;
        Name = request.Name;
        Badge = request.Protocol == "WebSocket" ? "WS" : request.Method;
        SourceRemoved = request.SourceRemoved;
        BadgeColor = request.Protocol == "WebSocket" ? "#BBA3FF" : request.Method switch
        {
            "GET" => "#7CD9B2",
            "POST" => "#F1CA76",
            "DELETE" => "#F18B96",
            "PUT" or "PATCH" => "#8CB8F8",
            _ => "#B6B9C6"
        };
    }
}
