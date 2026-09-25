using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using LaserWorks.Desktop.ViewModels;
using LaserWorks.Localization;
using LaserWorks.Reporting.Core;

namespace LaserWorks.Desktop.Views;

public partial class ReportsView : UserControl
{
    private ReportsViewModel? _vm;

    public ReportsView()
    {
        InitializeComponent();
        grid.LoadingRow += (_, e) =>
        {
            e.Row.Classes.Remove("warning"); e.Row.Classes.Remove("negative"); e.Row.Classes.Remove("subtotal");
            if (e.Row.DataContext is ReportRow r)
            {
                var cls = r.Style switch { RowStyle.Warning => "warning", RowStyle.Negative => "negative", RowStyle.Subtotal or RowStyle.Header => "subtotal", _ => null };
                if (cls != null) e.Row.Classes.Add(cls);
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.TableChanged -= Rebuild;
        _vm = DataContext as ReportsViewModel;
        if (_vm != null) _vm.TableChanged += Rebuild;
    }

    private void Rebuild(object? sender, EventArgs e)
    {
        grid.ItemsSource = null;
        grid.Columns.Clear();
        var table = _vm?.Table;
        if (table == null) return;
        var loc = Loc.Instance;
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            var kind = col.Kind;
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = loc[col.HeaderKey],
                Binding = new Binding($"Cells[{i}]") { Converter = new FuncValueConverter<object?, string>(v => CellFormatter.Format(v, kind)) },
                Width = new DataGridLength(Math.Max(0.5, col.Width), DataGridLengthUnitType.Star),
                MinWidth = CellFormatter.IsNumeric(kind) ? 90 : 70
            });
        }
        var rows = table.Rows.ToList();
        if (table.HasTotals)
        {
            var totals = table.Totals();
            totals[0] = loc["Common.Total"];
            rows.Add(new ReportRow(totals, RowStyle.Subtotal));
        }
        grid.ItemsSource = rows;
    }
}
