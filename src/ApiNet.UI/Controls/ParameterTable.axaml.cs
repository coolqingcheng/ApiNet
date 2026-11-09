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
    public static readonly StyledProperty<double> MinTableWidthProperty = AvaloniaProperty.Register<ParameterTable, double>(nameof(MinTableWidth), 560);
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ParameterTable, string>(nameof(Title), "Query Params");
    public static readonly StyledProperty<Func<Task<string?>>?> PickFileProperty = AvaloniaProperty.Register<ParameterTable, Func<Task<string?>>?>(nameof(PickFile));
    public static readonly StyledProperty<ParameterTableViewModel?> ModelProperty = AvaloniaProperty.Register<ParameterTable, ParameterTableViewModel?>(nameof(Model));
    public ParameterTableViewModel Model { get => GetValue(ModelProperty)!; set => SetValue(ModelProperty, value); }
    public ObservableCollection<ParameterRowViewModel>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public bool AllowFiles { get => GetValue(AllowFilesProperty); set => SetValue(AllowFilesProperty, value); }
    public double MinTableWidth { get => GetValue(MinTableWidthProperty); set => SetValue(MinTableWidthProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Func<Task<string?>>? PickFile { get => GetValue(PickFileProperty); set => SetValue(PickFileProperty, value); }
    public ParameterTable()
    {
        Model = new ParameterTableViewModel(() => PickFile?.Invoke() ?? Task.FromResult<string?>(null));
        InitializeComponent();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowsProperty && Model is not null) Model.SetRows(Rows);
    }
}
