using Avalonia;
using Avalonia.Controls;

namespace LaserWorks.Desktop.Controls;

public partial class ListToolbar : UserControl
{
    public static readonly StyledProperty<DataGrid?> GridProperty = AvaloniaProperty.Register<ListToolbar, DataGrid?>(nameof(Grid));
    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<ListToolbar, object?>(nameof(Actions));
    public static readonly StyledProperty<object?> FiltersProperty = AvaloniaProperty.Register<ListToolbar, object?>(nameof(Filters));

    public ListToolbar() => InitializeComponent();

    public DataGrid? Grid { get => GetValue(GridProperty); set => SetValue(GridProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public object? Filters { get => GetValue(FiltersProperty); set => SetValue(FiltersProperty, value); }
}
