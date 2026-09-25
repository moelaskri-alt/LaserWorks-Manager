using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using LaserWorks.Domain.Entities;
using LaserWorks.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LaserWorks.Reporting.Core;

public static class CellFormatter
{
    public static string Format(object? value, ColumnKind kind, int decimals = 2)
    {
        var loc = Loc.Instance;
        return value switch
        {
            null => "",
            decimal d => kind switch
            {
                ColumnKind.Money => loc.Money(d, decimals),
                ColumnKind.Percent => loc.Percent(d),
                ColumnKind.Integer => d.ToString("#,0", CultureInfo.InvariantCulture),
                _ => loc.Number(d)
            },
            int i => i.ToString("#,0", CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double db => db.ToString("#,0.##", CultureInfo.InvariantCulture),
            DateTime dt => loc.Date(dt),
            bool b => b ? loc["Common.Yes"] : loc["Common.No"],
            Enum e => loc.Enum(e),
            _ => value.ToString() ?? ""
        };
    }

    public static bool IsNumeric(ColumnKind k) => k is ColumnKind.Integer or ColumnKind.Number or ColumnKind.Money or ColumnKind.Percent;
}

public static class PdfFonts
{
    private static bool _registered;
    public const string Latin = "Noto Sans";
    public const string Arabic = "Noto Sans Arabic";

    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (typeof(PdfFonts))
        {
            if (_registered) return;
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = false;
            var asm = typeof(PdfFonts).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var s = asm.GetManifestResourceStream(name)!;
                QuestPDF.Drawing.FontManager.RegisterFontFromStream(s);
            }
            _registered = true;
        }
    }
}

public static class PdfExporter
{
    public static void Export(ReportTable table, CompanySettings company, string path) => File.WriteAllBytes(path, Render(table, company));

    public static byte[] Render(ReportTable table, CompanySettings company)
    {
        PdfFonts.EnsureRegistered();
        var loc = Loc.Instance;
        var rtl = loc.IsRightToLeft;
        var landscape = table.Columns.Count > 7 || table.Columns.Sum(c => c.Width) > 9;
        var totals = table.HasTotals ? table.Totals() : null;
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Latin, PdfFonts.Arabic).FontSize(8.5f));
            if (rtl) page.ContentFromRightToLeft();
            page.Header().Element(h => Header(h, company, table.Title, table.Parameters));
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { foreach (var c in table.Columns) cd.RelativeColumn(c.Width); });
                    t.Header(h =>
                    {
                        foreach (var c in table.Columns)
                            h.Cell().Background("#1F3A5F").Padding(4).AlignMiddle().Element(e => CellAlign(e, c.Kind)).Text(loc[c.HeaderKey]).FontColor(Colors.White).SemiBold();
                    });
                    var i = 0;
                    foreach (var r in table.Rows)
                    {
                        var bg = r.Style switch
                        {
                            RowStyle.Negative => "#FDECEC",
                            RowStyle.Warning => "#FFF6E0",
                            RowStyle.Subtotal => "#E8EEF6",
                            RowStyle.Header => "#DDE5F0",
                            _ => i++ % 2 == 0 ? "#FFFFFF" : "#F5F7FA"
                        };
                        for (int ci = 0; ci < table.Columns.Count; ci++)
                        {
                            var c = table.Columns[ci];
                            var text = ci < r.Cells.Length ? CellFormatter.Format(r.Cells[ci], c.Kind, company.DecimalPlaces) : "";
                            var cell = t.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#E1E6EE").Padding(3).Element(e => CellAlign(e, c.Kind)).Text(text);
                            if (r.Style is RowStyle.Subtotal or RowStyle.Header) cell.SemiBold();
                            if (r.Cells.ElementAtOrDefault(ci) is decimal d && d < 0 && CellFormatter.IsNumeric(c.Kind)) cell.FontColor("#B42318");
                        }
                    }
                    if (totals != null)
                        for (int ci = 0; ci < table.Columns.Count; ci++)
                        {
                            var c = table.Columns[ci];
                            var text = ci == 0 ? loc["Common.Total"] : totals[ci] is decimal d ? CellFormatter.Format(d, c.Kind, company.DecimalPlaces) : "";
                            t.Cell().Background("#E8EEF6").BorderTop(1).BorderColor("#1F3A5F").Padding(3).Element(e => CellAlign(e, c.Kind)).Text(text).SemiBold();
                        }
                });
                if (table.Rows.Count == 0) col.Item().PaddingTop(20).AlignCenter().Text(loc["Report.NoData"]).FontColor(Colors.Grey.Darken1);
                foreach (var n in table.Notes) col.Item().PaddingTop(6).Text(n).FontSize(8).FontColor(Colors.Grey.Darken2);
            });
            page.Footer().Element(f => Footer(f));
        })).GeneratePdf();
    }

    internal static IContainer CellAlign(IContainer e, ColumnKind k) => CellFormatter.IsNumeric(k) ? e.AlignRight() : e.AlignLeft();

    internal static void Header(IContainer h, CompanySettings company, string title, IEnumerable<string> parameters)
    {
        var loc = Loc.Instance;
        h.Column(c =>
        {
            c.Item().Row(r =>
            {
                r.RelativeItem().Column(cc =>
                {
                    cc.Item().Text(company.CompanyName).FontSize(13).Bold().FontColor("#1F3A5F");
                    var line = string.Join("  •  ", new[] { company.Address, company.Phone, company.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (line.Length > 0) cc.Item().Text(line).FontSize(8).FontColor(Colors.Grey.Darken2);
                    if (!string.IsNullOrWhiteSpace(company.TaxNumber)) cc.Item().Text($"{loc["Company.TaxNumber"]}: {company.TaxNumber}").FontSize(8).FontColor(Colors.Grey.Darken2);
                });
                r.ConstantItem(200).AlignRight().Column(cc =>
                {
                    cc.Item().AlignRight().Text(title).FontSize(14).Bold();
                    cc.Item().AlignRight().Text($"{loc["Report.GeneratedAt"]}: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
            });
            var p = parameters.ToList();
            if (p.Count > 0) c.Item().PaddingTop(4).Text(string.Join("   |   ", p)).FontSize(8).FontColor(Colors.Grey.Darken2);
            c.Item().PaddingTop(4).LineHorizontal(1.2f).LineColor("#1F3A5F");
        });
    }

    internal static void Footer(IContainer f)
    {
        var loc = Loc.Instance;
        f.AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
            t.Span("LaserWorks Manager — ");
            t.Span(loc["Report.Page"] + " ");
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });
    }
}

public static class ExcelExporter
{
    public static void Export(ReportTable table, CompanySettings company, string path)
    {
        using var wb = new XLWorkbook();
        var loc = Loc.Instance;
        var name = new string(table.Title.Where(ch => !"[]*?/\\:".Contains(ch)).ToArray());
        if (name.Length > 31) name = name[..31];
        if (string.IsNullOrWhiteSpace(name)) name = "Report";
        var ws = wb.Worksheets.Add(name);
        ws.RightToLeft = loc.IsRightToLeft;
        ws.Cell(1, 1).Value = company.CompanyName;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(2, 1).Value = table.Title;
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = string.Join("  |  ", table.Parameters);
        var headerRow = 5;
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = loc[table.Columns[c].HeaderKey];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3A5F");
            cell.Style.Font.FontColor = XLColor.White;
        }
        var row = headerRow + 1;
        foreach (var r in table.Rows)
        {
            for (int c = 0; c < table.Columns.Count; c++)
                SetCell(ws.Cell(row, c + 1), c < r.Cells.Length ? r.Cells[c] : null, table.Columns[c].Kind, company.DecimalPlaces);
            if (r.Style == RowStyle.Negative) ws.Range(row, 1, row, table.Columns.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEC");
            else if (r.Style == RowStyle.Warning) ws.Range(row, 1, row, table.Columns.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF6E0");
            else if (r.Style is RowStyle.Subtotal or RowStyle.Header) ws.Range(row, 1, row, table.Columns.Count).Style.Font.Bold = true;
            row++;
        }
        if (table.HasTotals)
        {
            var totals = table.Totals();
            ws.Cell(row, 1).Value = loc["Common.Total"];
            for (int c = 1; c < table.Columns.Count; c++)
                if (totals[c] is decimal d) SetCell(ws.Cell(row, c + 1), d, table.Columns[c].Kind, company.DecimalPlaces);
            ws.Range(row, 1, row, table.Columns.Count).Style.Font.Bold = true;
            ws.Range(row, 1, row, table.Columns.Count).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }
        if (table.Rows.Count > 0) ws.Range(headerRow, 1, row - 1, table.Columns.Count).SetAutoFilter();
        ws.Columns().AdjustToContents(headerRow, row, 8, 60);
        ws.SheetView.FreezeRows(headerRow);
        wb.SaveAs(path);
    }

    private static void SetCell(IXLCell cell, object? v, ColumnKind kind, int decimals)
    {
        switch (v)
        {
            case null: return;
            case decimal d:
                cell.Value = kind == ColumnKind.Percent ? d / 100m : d;
                cell.Style.NumberFormat.Format = kind switch
                {
                    ColumnKind.Money => "#,##0." + new string('0', Math.Max(decimals, 0)),
                    ColumnKind.Percent => "0.00%",
                    ColumnKind.Integer => "#,##0",
                    _ => "#,##0.###"
                };
                break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
            default: cell.Value = CellFormatter.Format(v, kind, decimals); break;
        }
    }
}

public static class CsvExporter
{
    public static void Export(ReportTable table, string path)
    {
        var loc = Loc.Instance;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Columns.Select(c => Esc(loc[c.HeaderKey]))));
        foreach (var r in table.Rows)
            sb.AppendLine(string.Join(",", table.Columns.Select((c, i) => Esc(Raw(i < r.Cells.Length ? r.Cells[i] : null)))));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Raw(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Enum e => Loc.Instance.Enum(e),
        bool b => b ? "1" : "0",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };

    private static string Esc(string s) => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
