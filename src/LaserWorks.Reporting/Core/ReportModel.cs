using LaserWorks.Application.Common;

namespace LaserWorks.Reporting.Core;

public enum ColumnKind { Text, Integer, Number, Money, Percent, Date }

public sealed record ReportColumn(string Key, string HeaderKey, ColumnKind Kind = ColumnKind.Text, float Width = 1f, bool Total = false);

public enum RowStyle { Normal, Warning, Negative, Subtotal, Header }

public sealed class ReportRow
{
    public ReportRow(object?[] cells, RowStyle style = RowStyle.Normal, string? sourceType = null, long? sourceId = null)
    {
        Cells = cells; Style = style; SourceType = sourceType; SourceId = sourceId;
    }

    public object?[] Cells { get; }
    public RowStyle Style { get; }
    /// <summary>Optional link to the source document for drill-down (e.g. "Job", 42).</summary>
    public string? SourceType { get; }
    public long? SourceId { get; }
}

/// <summary>A generic tabular report result that every exporter (screen, PDF, Excel, CSV, print) understands.</summary>
public sealed class ReportTable
{
    public string TitleKey { get; init; } = "";
    public string Title { get; set; } = "";
    public List<string> Parameters { get; } = new();
    public List<ReportColumn> Columns { get; } = new();
    public List<ReportRow> Rows { get; } = new();
    public bool ShowTotals { get; set; } = true;
    public List<string> Notes { get; } = new();

    public ReportTable Col(string key, string headerKey, ColumnKind kind = ColumnKind.Text, float width = 1f, bool total = false)
    {
        Columns.Add(new ReportColumn(key, headerKey, kind, width, total));
        return this;
    }

    public ReportTable Add(params object?[] cells) { Rows.Add(new ReportRow(cells)); return this; }

    public ReportTable AddRow(RowStyle style, string? sourceType, long? sourceId, params object?[] cells) { Rows.Add(new ReportRow(cells, style, sourceType, sourceId)); return this; }

    public object?[] Totals()
    {
        var totals = new object?[Columns.Count];
        for (int i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Total) continue;
            decimal sum = 0;
            foreach (var r in Rows.Where(r => r.Style is not (RowStyle.Subtotal or RowStyle.Header)))
                if (i < r.Cells.Length && r.Cells[i] is decimal d) sum += d;
                else if (i < r.Cells.Length && r.Cells[i] is int n) sum += n;
            totals[i] = sum;
        }
        return totals;
    }

    public bool HasTotals => ShowTotals && Columns.Any(c => c.Total) && Rows.Count > 0;
}

[Flags]
public enum ReportFilterKind
{
    None = 0, DateRange = 1, AsOfDate = 2, Customer = 4, Job = 8, Machine = 16, Material = 32, Status = 64, Account = 128, Supplier = 256
}

public sealed record ReportFilter(
    DateRange Range, DateTime AsOf, long? CustomerId = null, long? JobId = null, long? MachineId = null, long? MaterialId = null, string? Status = null, long? AccountId = null, long? SupplierId = null);

public sealed record ReportDefinition(
    string Key, string TitleKey, string CategoryKey, ReportFilterKind Filters, Func<ReportFilter, Task<ReportTable>> Build, IReadOnlyList<(string Value, string LabelKey)>? StatusOptions = null, ReportFilterKind Required = ReportFilterKind.None);
