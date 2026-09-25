using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Localization;
using LaserWorks.Reporting.Core;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LaserWorks.Reporting.Documents;

/// <summary>Professional PDFs for customer-facing and management documents.</summary>
public static class DocumentRenderer
{
    private static Loc L => Loc.Instance;
    private const string Accent = "#1F3A5F";

    private static void Page(PageDescriptor page, CompanySettings company, string title, IEnumerable<string> parameters)
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Latin, PdfFonts.Arabic).FontSize(9.5f));
        if (L.IsRightToLeft) page.ContentFromRightToLeft();
        page.Header().Element(h => PdfExporter.Header(h, company, title, parameters));
        page.Footer().Element(PdfExporter.Footer);
    }

    private static void KeyValues(ColumnDescriptor col, params (string Key, string? Value)[] items)
    {
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { c.ConstantColumn(120); c.RelativeColumn(); });
            foreach (var (k, v) in items.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
            {
                t.Cell().PaddingVertical(2).Text(L[k]).FontColor(Colors.Grey.Darken2);
                t.Cell().PaddingVertical(2).Text(v!).SemiBold();
            }
        });
    }

    private static void AmountRow(TableDescriptor t, string label, decimal amount, int decimals, bool strong = false)
    {
        var a = t.Cell().PaddingVertical(3).Text(label);
        var b = t.Cell().PaddingVertical(3).AlignRight().Text(L.Money(amount, decimals));
        if (strong) { a.Bold().FontSize(11); b.Bold().FontSize(11); }
    }

    public static byte[] Quotation(Quotation q, CompanySettings company)
    {
        PdfFonts.EnsureRegistered();
        var d = company.DecimalPlaces;
        return Document.Create(doc => doc.Page(page =>
        {
            Page(page, company, L["Doc.Quotation"], new[] { $"{L["Col.Number"]}: {q.Number} v{q.VersionNo}", $"{L["Col.Date"]}: {L.Date(q.Date)}" });
            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(10);
                col.Item().Row(r =>
                {
                    r.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(c =>
                    {
                        c.Item().Text(L["Doc.QuotedTo"]).FontColor(Colors.Grey.Darken1).FontSize(8);
                        c.Item().Text(q.Customer?.Name ?? "").Bold().FontSize(11);
                        if (!string.IsNullOrWhiteSpace(q.Customer?.Address)) c.Item().Text(q.Customer!.Address!);
                        if (!string.IsNullOrWhiteSpace(q.Customer?.Phone)) c.Item().Text(q.Customer!.Phone!);
                        if (!string.IsNullOrWhiteSpace(q.Customer?.TaxNumber)) c.Item().Text($"{L["Company.TaxNumber"]}: {q.Customer!.TaxNumber}");
                    });
                    r.ConstantItem(12);
                    r.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(c => KeyValues(c,
                        ("Col.ValidUntil", L.Date(q.ValidUntil)), ("Col.DeliveryTime", q.DeliveryDays > 0 ? L.Format("Doc.DeliveryDays", q.DeliveryDays) : null),
                        ("Col.PaymentTerms", q.PaymentTerms), ("Col.Request", q.Request?.Number)));
                });
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(28); c.RelativeColumn(5); c.RelativeColumn(1.2f); c.RelativeColumn(1.6f); c.RelativeColumn(1.8f); });
                    t.Header(h =>
                    {
                        foreach (var k in new[] { "#", L["Col.Description"], L["Col.Quantity"], L["Col.UnitPrice"], L["Col.Amount"] })
                            h.Cell().Background(Accent).Padding(5).Text(k).FontColor(Colors.White).SemiBold();
                    });
                    t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("1");
                    t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(q.Description);
                    t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(L.Number(q.Quantity));
                    t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(L.Money(q.Quantity == 0 ? 0 : q.SellingPrice / q.Quantity, d));
                    t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(L.Money(q.SellingPrice, d));
                });
                col.Item().AlignRight().Width(260).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    AmountRow(t, L["Col.Subtotal"], q.SellingPrice, d);
                    if (q.DiscountAmount != 0) AmountRow(t, L["Col.Discount"], -q.DiscountAmount, d);
                    AmountRow(t, $"{L["Col.Tax"]} ({L.Percent(q.TaxRate)})", q.TaxAmount, d);
                    AmountRow(t, $"{L["Col.Total"]} ({company.CurrencyCode})", q.Total, d, strong: true);
                });
                if (!string.IsNullOrWhiteSpace(q.Notes)) col.Item().Text($"{L["Col.Notes"]}: {q.Notes}");
                col.Item().PaddingTop(20).Text(L["Doc.QuotationFooter"]).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(30).Row(r =>
                {
                    r.RelativeItem().Column(c => { c.Item().LineHorizontal(0.5f); c.Item().Text(L["Doc.AuthorizedSignature"]).FontSize(8); });
                    r.ConstantItem(60);
                    r.RelativeItem().Column(c => { c.Item().LineHorizontal(0.5f); c.Item().Text(L["Doc.CustomerAcceptance"]).FontSize(8); });
                });
            });
        })).GeneratePdf();
    }

    public static byte[] Invoice(SalesInvoice inv, CompanySettings company)
    {
        PdfFonts.EnsureRegistered();
        var d = company.DecimalPlaces;
        return Document.Create(doc => doc.Page(page =>
        {
            Page(page, company, L["Doc.TaxInvoice"], new[] { $"{L["Col.Number"]}: {inv.Number}", $"{L["Col.Date"]}: {L.Date(inv.Date)}", $"{L["Col.DueDate"]}: {L.Date(inv.DueDate)}" });
            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(10);
                if (inv.Status != Domain.Enums.DocumentStatus.Posted)
                    col.Item().Background("#FFF6E0").Padding(6).Text(L["Doc.DraftWatermark"]).Bold().FontColor("#B54708");
                col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(c =>
                {
                    c.Item().Text(L["Doc.BillTo"]).FontColor(Colors.Grey.Darken1).FontSize(8);
                    c.Item().Text(inv.Customer?.Name ?? "").Bold().FontSize(11);
                    if (!string.IsNullOrWhiteSpace(inv.Customer?.Address)) c.Item().Text(inv.Customer!.Address!);
                    if (!string.IsNullOrWhiteSpace(inv.Customer?.TaxNumber)) c.Item().Text($"{L["Company.TaxNumber"]}: {inv.Customer!.TaxNumber}");
                });
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(24); c.RelativeColumn(4.5f); c.RelativeColumn(1); c.RelativeColumn(1.4f); c.RelativeColumn(1.2f); c.RelativeColumn(1.2f); c.RelativeColumn(1.6f); });
                    t.Header(h =>
                    {
                        foreach (var k in new[] { "#", L["Col.Description"], L["Col.Quantity"], L["Col.UnitPrice"], L["Col.Discount"], L["Col.Tax"], L["Col.Amount"] })
                            h.Cell().Background(Accent).Padding(5).Text(k).FontColor(Colors.White).SemiBold();
                    });
                    var i = 1;
                    foreach (var l in inv.Lines)
                    {
                        IContainer C(IContainer x) => x.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5);
                        t.Cell().Element(C).Text((i++).ToString());
                        t.Cell().Element(C).Text(l.Description);
                        t.Cell().Element(C).AlignRight().Text(L.Number(l.Quantity));
                        t.Cell().Element(C).AlignRight().Text(L.Money(l.UnitPrice, d));
                        t.Cell().Element(C).AlignRight().Text(L.Money(l.DiscountAmount, d));
                        t.Cell().Element(C).AlignRight().Text(L.Money(l.TaxAmount, d));
                        t.Cell().Element(C).AlignRight().Text(L.Money(l.LineTotal, d));
                    }
                });
                col.Item().AlignRight().Width(260).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    AmountRow(t, L["Col.Subtotal"], inv.Subtotal, d);
                    if (inv.DiscountAmount != 0) AmountRow(t, L["Col.Discount"], -inv.DiscountAmount, d);
                    AmountRow(t, L["Col.Tax"], inv.TaxAmount, d);
                    AmountRow(t, $"{L["Col.Total"]} ({company.CurrencyCode})", inv.Total, d, strong: true);
                    if (inv.PaidAmount != 0) AmountRow(t, L["Col.Paid"], inv.PaidAmount, d);
                    if (inv.ReturnedAmount != 0) AmountRow(t, L["Col.Returned"], inv.ReturnedAmount, d);
                    if (inv.PaidAmount != 0 || inv.ReturnedAmount != 0) AmountRow(t, L["Col.Balance"], inv.Balance, d, strong: true);
                });
                if (!string.IsNullOrWhiteSpace(inv.Notes)) col.Item().Text($"{L["Col.Notes"]}: {inv.Notes}");
            });
        })).GeneratePdf();
    }

    public static byte[] JobCostSheet(JobCostSheet s, CompanySettings company)
    {
        PdfFonts.EnsureRegistered();
        var d = company.DecimalPlaces;
        var job = s.Job;
        return Document.Create(doc => doc.Page(page =>
        {
            Page(page, company, L["Rpt.JobCostSheet"], new[] { $"{L["Col.Job"]}: {job.Number}", $"{L["Col.Customer"]}: {s.Customer}", $"{L["Col.Status"]}: {L.Enum(job.Status)}" });
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);
                col.Item().Text(job.Title).Bold().FontSize(12);
                col.Item().Row(r =>
                {
                    void Tile(string label, string value, string color) => r.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(c =>
                    {
                        c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(value).Bold().FontSize(12).FontColor(color);
                    });
                    Tile(s.RevenueIsInvoiced ? L["Col.Revenue"] : L["Col.SellingPrice"], L.Money(s.Revenue, d), Accent);
                    r.ConstantItem(6);
                    Tile(L["Col.EstimatedCost"], L.Money(s.Variance.EstimatedTotal, d), Accent);
                    r.ConstantItem(6);
                    Tile(L["Col.ActualCost"], L.Money(s.ActualCost, d), s.ActualCost > s.Variance.EstimatedTotal ? "#B42318" : Accent);
                    r.ConstantItem(6);
                    Tile(L["Col.GrossProfit"], $"{L.Money(s.GrossProfit, d)} ({L.Percent(s.MarginPercent)})", s.GrossProfit < 0 ? "#B42318" : "#067647");
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Column(c => KeyValues(c, ("Col.Quantity", L.Number(job.Quantity)), ("Col.OrderDate", L.Date(job.OrderDate)), ("Col.DueDate", L.Date(job.DueDate)),
                        ("Col.DeliveredAt", L.Date(job.DeliveredAt)), ("Col.Quotation", s.QuotationNumber), ("Col.Estimate", s.EstimateNumber)));
                    r.RelativeItem().Column(c => KeyValues(c, ("Col.Machine", job.Machine?.Name), ("Col.Operator", job.Operator?.Name), ("Col.ApprovedDesign", job.DesignRevision?.RevisionLabel),
                        ("Col.PlannedHours", L.Number(s.PlannedHours)), ("Col.ActualHours", L.Number(s.ActualHours)), ("Col.MachineHours", L.Number(s.MachineHours))));
                });
                col.Item().Text(L["Rpt.EstimatedVsActual"]).Bold().FontSize(11).FontColor(Accent);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(0.8f); });
                    t.Header(h =>
                    {
                        foreach (var k in new[] { "Col.Component", "Col.Estimated", "Col.Actual", "Col.Variance", "Col.VariancePercent" })
                            h.Cell().Background(Accent).Padding(4).Text(L[k]).FontColor(Colors.White).SemiBold();
                    });
                    foreach (var l in s.Variance.Lines)
                    {
                        var bg = l.Variance > 0 ? "#FFF6E0" : "#FFFFFF";
                        t.Cell().Background(bg).Padding(4).Text(L.Enum(l.Component));
                        t.Cell().Background(bg).Padding(4).AlignRight().Text(L.Money(l.Estimated, d));
                        t.Cell().Background(bg).Padding(4).AlignRight().Text(L.Money(l.Actual, d));
                        t.Cell().Background(bg).Padding(4).AlignRight().Text(L.Money(l.Variance, d)).FontColor(l.Variance > 0 ? "#B42318" : "#067647");
                        t.Cell().Background(bg).Padding(4).AlignRight().Text(L.Percent(l.VariancePercent));
                    }
                    t.Cell().Background("#E8EEF6").Padding(4).Text(L["Common.Total"]).Bold();
                    t.Cell().Background("#E8EEF6").Padding(4).AlignRight().Text(L.Money(s.Variance.EstimatedTotal, d)).Bold();
                    t.Cell().Background("#E8EEF6").Padding(4).AlignRight().Text(L.Money(s.Variance.ActualTotal, d)).Bold();
                    t.Cell().Background("#E8EEF6").Padding(4).AlignRight().Text(L.Money(s.Variance.Variance, d)).Bold();
                    t.Cell().Background("#E8EEF6").Padding(4).AlignRight().Text(L.Percent(s.Variance.VariancePercent)).Bold();
                });
                if (s.Variance.MainDriver is { } drv) col.Item().Text($"{L["Var.MainReason"]}: {L.Enum(drv)}").Italic();
                if (s.Materials.Count > 0)
                {
                    col.Item().Text(L["Col.MaterialsIssued"]).Bold().FontSize(11).FontColor(Accent);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                        foreach (var k in new[] { "Col.Material", "Col.Quantity", "Col.UnitCost", "Col.Value" }) t.Cell().BorderBottom(1).Padding(3).Text(L[k]).SemiBold();
                        foreach (var m in s.Materials)
                        {
                            t.Cell().Padding(3).Text($"{m.MaterialCode} {m.MaterialName}");
                            t.Cell().Padding(3).AlignRight().Text(L.Number(m.NetQuantity));
                            t.Cell().Padding(3).AlignRight().Text(L.Money(m.AverageIssueCost, d));
                            t.Cell().Padding(3).AlignRight().Text(L.Money(m.NetValue, d));
                        }
                    });
                }
                col.Item().Text(L["Col.CostEntries"]).Bold().FontSize(11).FontColor(Accent);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(0.9f); c.RelativeColumn(1.1f); c.RelativeColumn(3.5f); c.RelativeColumn(0.7f); c.RelativeColumn(1.1f); });
                    foreach (var k in new[] { "Col.Date", "Col.Component", "Col.Description", "Col.Hours", "Col.Amount" }) t.Cell().BorderBottom(1).Padding(3).Text(L[k]).SemiBold();
                    foreach (var e in s.Entries)
                    {
                        t.Cell().Padding(2).Text(L.Date(e.Date)).FontSize(8);
                        t.Cell().Padding(2).Text(L.Enum(e.Component)).FontSize(8);
                        t.Cell().Padding(2).Text(e.Description ?? "").FontSize(8);
                        t.Cell().Padding(2).AlignRight().Text(e.Hours == 0 ? "" : L.Number(e.Hours)).FontSize(8);
                        t.Cell().Padding(2).AlignRight().Text(L.Money(e.Amount, d)).FontSize(8);
                    }
                });
                if (s.Scrap.Count > 0)
                    col.Item().Text($"{L["Nav.ScrapRework"]}: {s.Scrap.Count} — {L.Money(s.Scrap.Sum(x => x.Cost), d)}").FontSize(9);
                if (s.Quality.Count > 0)
                {
                    var q = s.Quality[^1];
                    col.Item().Text($"{L["Nav.Quality"]}: {L.Enum(q.Status)} — {L["Col.Accepted"]} {L.Number(q.QuantityAccepted)} / {L["Col.Produced"]} {L.Number(q.QuantityProduced)}").FontSize(9);
                }
            });
        })).GeneratePdf();
    }
}
