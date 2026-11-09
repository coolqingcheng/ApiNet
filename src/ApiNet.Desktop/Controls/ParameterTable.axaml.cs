using System.Collections.ObjectModel;
using System.Windows.Input;
using ApiNet.Desktop.ViewModels.Tabs;
using Avalonia;
using Avalonia.Controls;

namespace ApiNet.Desktop.Controls;

public partial class ParameterTable : UserControl
{
    public static readonly StyledProperty<ObservableCollection<ParameterRowViewModel>?> RowsProperty = AvaloniaProperty.Register<ParameterTable, ObservableCollection<ParameterRowViewModel>?>(nameof(Rows));
    public static readonly StyledProperty<ICommand?> AddCommandProperty = AvaloniaProperty.Register<ParameterTable, ICommand?>(nameof(AddCommand));
    public ObservableCollection<ParameterRowViewModel>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public ParameterTable() => InitializeComponent();
}
