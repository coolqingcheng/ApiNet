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
    [ObservableProperty] private bool _isExportSelected;
    public WorkspaceNodeViewModel(FolderDefinition folder)
    {
        Id = folder.Id;
        IsFolder = true;
        Name = folder.Name;
        Badge = "▱";
        BadgeColor = ApiNet.Desktop.Services.ThemeService.MethodColor("");
    }
    public WorkspaceNodeViewModel(RequestDefinition request)
    {
        Id = request.Id;
        Name = request.Name;
        Badge = request.Protocol == "WebSocket" ? "WS" : request.Method;
        SourceRemoved = request.SourceRemoved;
        BadgeColor = ApiNet.Desktop.Services.ThemeService.MethodColor(Badge);
    }
}
