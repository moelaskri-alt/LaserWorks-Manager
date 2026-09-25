using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LaserWorks.Desktop.ViewModels;

namespace LaserWorks.Desktop;

/// <summary>DataGrid helpers: server-side sorting, open-on-double-click/Enter, row highlight classes.</summary>
public static class GridBehaviors
{
    public static readonly AttachedProperty<bool> ServerSortProperty = AvaloniaProperty.RegisterAttached<DataGrid, bool>("ServerSort", typeof(GridBehaviors));
    public static readonly AttachedProperty<ICommand?> OpenCommandProperty = AvaloniaProperty.RegisterAttached<DataGrid, ICommand?>("OpenCommand", typeof(GridBehaviors));
    public static readonly AttachedProperty<bool> RowClassesProperty = AvaloniaProperty.RegisterAttached<DataGrid, bool>("RowClasses", typeof(GridBehaviors));

    public static bool GetServerSort(DataGrid g) => g.GetValue(ServerSortProperty);
    public static void SetServerSort(DataGrid g, bool v) => g.SetValue(ServerSortProperty, v);
    public static ICommand? GetOpenCommand(DataGrid g) => g.GetValue(OpenCommandProperty);
    public static void SetOpenCommand(DataGrid g, ICommand? v) => g.SetValue(OpenCommandProperty, v);
    public static bool GetRowClasses(DataGrid g) => g.GetValue(RowClassesProperty);
    public static void SetRowClasses(DataGrid g, bool v) => g.SetValue(RowClassesProperty, v);

    static GridBehaviors()
    {
        ServerSortProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            if (e.NewValue is true) g.Sorting += OnSorting;
            else g.Sorting -= OnSorting;
        });
        OpenCommandProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            g.DoubleTapped -= OnDoubleTapped;
            g.KeyDown -= OnKeyDown;
            if (e.NewValue != null)
            {
                g.DoubleTapped += OnDoubleTapped;
                g.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            }
        });
        RowClassesProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            if (e.NewValue is true) g.LoadingRow += OnLoadingRow;
            else g.LoadingRow -= OnLoadingRow;
        });
    }

    private static void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (sender is DataGrid { DataContext: ISortableList list })
        {
            list.Sort(e.Column.SortMemberPath);
            e.Handled = true;
        }
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid g || g.SelectedItem == null) return;
        if (e.Source is Control c && c.FindAncestorOfType<DataGridColumnHeader>() != null) return;
        var cmd = GetOpenCommand(g);
        if (cmd?.CanExecute(g.SelectedItem) == true) cmd.Execute(g.SelectedItem);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not DataGrid g || g.SelectedItem == null) return;
        var cmd = GetOpenCommand(g);
        if (cmd?.CanExecute(g.SelectedItem) != true) return;
        cmd.Execute(g.SelectedItem);
        e.Handled = true;
    }

    private static void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (sender is not DataGrid g) return;
        e.Row.Classes.Remove("warning");
        e.Row.Classes.Remove("negative");
        e.Row.Classes.Remove("subtotal");
        var cls = (g.DataContext as IRowClassifier)?.RowClass(e.Row.DataContext!);
        if (!string.IsNullOrEmpty(cls)) e.Row.Classes.Add(cls);
    }

    private static T? FindAncestorOfType<T>(this Control c) where T : class
    {
        Visual? v = c;
        while (v != null)
        {
            if (v is T t) return t;
            v = v.GetVisualParentSafe();
        }
        return null;
    }

    private static Visual? GetVisualParentSafe(this Visual v) => Avalonia.VisualTree.VisualExtensions.GetVisualParent(v);
}

/// <summary>Button that lets the user show/hide columns of a DataGrid.</summary>
public sealed class ColumnChooser : Button
{
    public static readonly StyledProperty<DataGrid?> TargetProperty = AvaloniaProperty.Register<ColumnChooser, DataGrid?>(nameof(Target));

    public DataGrid? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnClick()
    {
        base.OnClick();
        if (Target == null) return;
        var menu = new MenuFlyout();
        foreach (var col in Target.Columns)
        {
            var header = col.Header?.ToString();
            if (string.IsNullOrWhiteSpace(header)) continue;
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = col.IsVisible, StaysOpenOnClick = true };
            var c = col;
            item.Click += (_, _) =>
            {
                if (c.IsVisible && Target.Columns.Count(x => x.IsVisible) <= 1) return;
                c.IsVisible = !c.IsVisible;
                item.IsChecked = c.IsVisible;
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(this);
    }
}
