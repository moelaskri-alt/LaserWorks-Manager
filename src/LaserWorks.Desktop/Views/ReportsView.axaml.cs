using Avalonia.Controls;
using Avalonia.Data;
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
            if (e.Row.DataContext is ReportGridRow r)
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
        var rows = table.Rows.Select(r => new ReportGridRow(r, table.Columns)).ToList();
        if (table.HasTotals)
        {
            var totals = table.Totals();
            totals[0] = loc["Common.Total"];
            rows.Add(new ReportGridRow(new ReportRow(totals, RowStyle.Subtotal), table.Columns));
        }
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            // wide enough for the column's content (sampled), so codes, names and flags are readable; wider tables scroll
            var content = rows.Take(500).Select(r => r.Text[i]).Where(t => t.Length > 0).Select(t => GridStandard.MeasureCell(grid, t, true)).DefaultIfEmpty(0).Max();
            var column = new DataGridTextColumn
            {
                Header = loc[col.HeaderKey],
                // one-way to pre-formatted text: the grid can never write into the report
                Binding = new Binding($"{nameof(ReportGridRow.Text)}[{i}]") { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                Width = new DataGridLength(Math.Max(0.5, col.Width), DataGridLengthUnitType.Star),
                MinWidth = Math.Max(CellFormatter.IsNumeric(col.Kind) ? 90 : 70, Math.Min(360, Math.Ceiling(content + 28)))
            };
            if (CellFormatter.IsNumeric(col.Kind)) column.CellStyleClasses.Add("num");
            grid.Columns.Add(column);
        }
        grid.ItemsSource = rows;
    }
}
