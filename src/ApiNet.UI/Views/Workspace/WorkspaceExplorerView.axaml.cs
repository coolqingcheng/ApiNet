using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ApiNet.Desktop.ViewModels;
using ApiNet.Desktop.ViewModels.Workspace;
namespace ApiNet.Desktop.Views.Workspace;

public partial class WorkspaceExplorerView : UserControl
{
    private static readonly DataFormat<string> NodeFormat = DataFormat.CreateStringApplicationFormat("apinet-node");
    private Point _press;
    private string? _dragId;
    public WorkspaceExplorerView()
    {
        InitializeComponent();
        RequestTree.PointerPressed += (_, e) => { _press = e.GetPosition(RequestTree); _dragId = (e.Source as Control)?.DataContext is WorkspaceNodeViewModel node ? node.Id : null; };
        RequestTree.PointerMoved += async (_, e) =>
        {
            var position = e.GetPosition(RequestTree);
            if (_dragId is null || !e.GetCurrentPoint(RequestTree).Properties.IsLeftButtonPressed || Math.Abs(position.X - _press.X) + Math.Abs(position.Y - _press.Y) < 8)
                return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(NodeFormat, _dragId));
            _dragId = null;
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        };
        RequestTree.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(NodeFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; });
        RequestTree.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (DataContext is not MainWindowViewModel model || e.DataTransfer.TryGetValue(NodeFormat) is not string id)
                return;
            var node = (e.Source as Control)?.DataContext as WorkspaceNodeViewModel;
            node ??= (e.Source as Control)?.FindAncestorOfType<TreeViewItem>()?.DataContext as WorkspaceNodeViewModel;
            await model.MoveNodeAsync(id, node?.IsFolder == true ? node.Id : null);
            e.Handled = true;
        });
    }
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (DataContext is MainWindowViewModel model)
            model.OpenSelectedCommand.Execute(null);
    }
}
