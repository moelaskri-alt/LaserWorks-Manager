using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record ChartPoint(string Label, decimal Value, long? Id = null, string? Key = null);

public sealed record DashboardData(
    decimal SalesToday, decimal SalesThisMonth, decimal SalesInRange, decimal GrossProfit, decimal GrossMarginPercent,
    int OpenJobs, int PendingQuotations, int JobsDueSoon, int OverdueJobs, decimal MaterialCost, decimal MachineCost, decimal ScrapPercent, decimal ReworkPercent,
    decimal OutstandingReceivables, decimal CashBalance, int LowStockItems,
    IReadOnlyList<ChartPoint> SalesTrend, IReadOnlyList<ChartPoint> GrossProfitTrend, IReadOnlyList<ChartPoint> JobsByStatus, IReadOnlyList<ChartPoint> CostComposition,
    IReadOnlyList<ChartPoint> TopCustomers, IReadOnlyList<ChartPoint> TopJobs, IReadOnlyList<ChartPoint> MaterialConsumption);

public sealed class DashboardService : ServiceBase
{
    public DashboardService(ServiceContext ctx) : base(ctx) { }

    public async Task<DashboardData> LoadAsync(DateRange range)
    {
        Demand(AppModule.Dashboard, Permission.View);
        await using var db = Factory.Create();
        var today = Now.Date;
        var month = DateRange.ThisMonth(today);

        async Task<decimal> NetSales(DateTime from, DateTime toExcl)
        {
            var inv = await db.SalesInvoices.Where(i => i.Status == DocumentStatus.Posted && i.Date >= from && i.Date < toExcl).SumAsync(i => i.Subtotal - i.DiscountAmount);
            var ret = await db.SalesReturns.Where(r => r.Status == DocumentStatus.Posted && r.Date >= from && r.Date < toExcl).SumAsync(r => r.Subtotal);
            return inv - ret;
        }

        var salesToday = await NetSales(today, today.AddDays(1));
        var salesMonth = await NetSales(month.From, month.ToExclusive);
        var salesRange = await NetSales(range.From.Date, range.ToExclusive);

        // GL based revenue & COGS for the range and trend
        var glLines = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date >= range.From.Date && l.JournalEntry.Date < range.ToExclusive
                        && (l.Account!.SystemKey == SystemAccounts.Sales || l.Account.SystemKey == SystemAccounts.SalesReturns || l.Account.SystemKey == SystemAccounts.COGS))
            .Select(l => new { l.JournalEntry!.Date, l.Account!.SystemKey, l.Debit, l.Credit }).ToListAsync();
        var revenue = glLines.Where(x => x.SystemKey != SystemAccounts.COGS).Sum(x => x.Credit - x.Debit);
        var cogs = glLines.Where(x => x.SystemKey == SystemAccounts.COGS).Sum(x => x.Debit - x.Credit);
        var gp = revenue - cogs;

        var daily = (range.ToExclusive - range.From.Date).TotalDays <= 62;
        Func<DateTime, DateTime> bucket = daily ? d => d.Date : d => new DateTime(d.Year, d.Month, 1);
        string Fmt(DateTime d) => daily ? d.ToString("MM-dd") : d.ToString("yyyy-MM");
        var buckets = new List<DateTime>();
        for (var d = bucket(range.From.Date); d < range.ToExclusive; d = daily ? d.AddDays(1) : d.AddMonths(1)) buckets.Add(d);
        if (buckets.Count > 400) buckets = buckets.TakeLast(400).ToList();
        var salesTrend = buckets.Select(b => new ChartPoint(Fmt(b), glLines.Where(x => bucket(x.Date) == b && x.SystemKey != SystemAccounts.COGS).Sum(x => x.Credit - x.Debit), Key: b.ToString("yyyy-MM-dd"))).ToList();
        var gpTrend = buckets.Select(b => new ChartPoint(Fmt(b), glLines.Where(x => bucket(x.Date) == b).Sum(x => x.SystemKey == SystemAccounts.COGS ? -(x.Debit - x.Credit) : x.Credit - x.Debit), Key: b.ToString("yyyy-MM-dd"))).ToList();

        var openJobs = await db.Jobs.CountAsync(j => j.Status < JobStatus.Delivered);
        var pendingQuotes = await db.Quotations.CountAsync(q => q.IsLatestVersion && (q.Status == QuotationStatus.Draft || q.Status == QuotationStatus.Sent));
        var dueLimit = today.AddDays(7);
        var dueSoon = await db.Jobs.CountAsync(j => j.Status < JobStatus.Delivered && j.DueDate != null && j.DueDate >= today && j.DueDate <= dueLimit);
        var overdue = await db.Jobs.CountAsync(j => j.Status < JobStatus.Delivered && j.DueDate != null && j.DueDate < today);

        var costs = await db.JobCostEntries.AsNoTracking().Where(e => e.Date >= range.From.Date && e.Date < range.ToExclusive)
            .GroupBy(e => e.Component).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync();
        decimal C(CostComponent c) => costs.Where(x => x.Key == c).Sum(x => x.Sum);
        var material = C(CostComponent.Material);
        var machine = C(CostComponent.Machine) + C(CostComponent.Maintenance);
        var scrap = C(CostComponent.Scrap);
        var rework = C(CostComponent.Rework);
        var totalCost = costs.Sum(x => x.Sum);

        var arId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AR)).Id;
        var ar = await db.JournalLines.Where(l => l.AccountId == arId && l.JournalEntry!.Status != JournalStatus.Draft).SumAsync(l => l.Debit - l.Credit);
        var cash = await db.JournalLines.Where(l => l.Account!.IsCashOrBank && l.JournalEntry!.Status != JournalStatus.Draft).SumAsync(l => l.Debit - l.Credit);
        var lowStock = await db.Materials.CountAsync(m => m.IsActive && m.ReorderLevel > 0 && m.QuantityOnHand <= m.ReorderLevel);

        var jobsByStatus = (await db.Jobs.GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .OrderBy(x => x.Key).Select(x => new ChartPoint(x.Key.ToString(), x.Count, Key: x.Key.ToString())).ToList();
        var composition = costs.Where(x => x.Sum != 0).OrderByDescending(x => x.Sum).Select(x => new ChartPoint(x.Key.ToString(), x.Sum, Key: x.Key.ToString())).ToList();

        var topCustomers = (await db.SalesInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Posted && i.Date >= range.From.Date && i.Date < range.ToExclusive)
                .GroupBy(i => new { i.CustomerId, i.Customer!.Name }).Select(g => new { g.Key.CustomerId, g.Key.Name, Sum = g.Sum(x => x.Subtotal - x.DiscountAmount) }).ToListAsync())
            .OrderByDescending(x => x.Sum).Take(7).Select(x => new ChartPoint(x.Name, x.Sum, x.CustomerId)).ToList();

        var topJobs = (await db.Jobs.AsNoTracking().Where(j => j.Status >= JobStatus.Invoiced && j.InvoicedAt >= range.From.Date && j.InvoicedAt < range.ToExclusive)
                .Select(j => new { j.Id, j.Number, j.InvoicedRevenue, j.ActualCost }).ToListAsync())
            .Select(j => new ChartPoint(j.Number, j.InvoicedRevenue - j.ActualCost, j.Id)).OrderByDescending(x => x.Value).Take(7).ToList();

        var matUse = await db.JobCostEntries.AsNoTracking().Where(e => e.Component == CostComponent.Material && e.MaterialId != null && e.Date >= range.From.Date && e.Date < range.ToExclusive)
            .GroupBy(e => e.MaterialId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync();
        var matNames = await db.Materials.AsNoTracking().Select(m => new { m.Id, m.Name }).ToDictionaryAsync(m => m.Id, m => m.Name);
        var materialChart = matUse.Where(x => x.Sum > 0).OrderByDescending(x => x.Sum).Take(7).Select(x => new ChartPoint(matNames.GetValueOrDefault(x.Key!.Value, "?"), x.Sum, x.Key)).ToList();

        return new DashboardData(salesToday, salesMonth, salesRange, gp, Money.Percent(gp, revenue), openJobs, pendingQuotes, dueSoon, overdue, material, machine,
            Money.Percent(scrap, material + scrap), Money.Percent(rework, totalCost), ar, cash, lowStock,
            salesTrend, gpTrend, jobsByStatus, composition, topCustomers, topJobs, materialChart);
    }
}
