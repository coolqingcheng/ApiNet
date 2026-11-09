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
    public static readonly StyledProperty<bool> AllowFilesProperty = AvaloniaProperty.Register<ParameterTable, bool>(nameof(AllowFiles));
    public static readonly StyledProperty<double> MinTableWidthProperty = AvaloniaProperty.Register<ParameterTable, double>(nameof(MinTableWidth));
    public ObservableCollection<ParameterRowViewModel>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public bool AllowFiles { get => GetValue(AllowFilesProperty); set => SetValue(AllowFilesProperty, value); }
    public double MinTableWidth { get => GetValue(MinTableWidthProperty); set => SetValue(MinTableWidthProperty, value); }
    public ParameterTable() => InitializeComponent();
}
