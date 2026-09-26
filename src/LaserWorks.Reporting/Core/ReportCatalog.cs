using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;
using LaserWorks.Localization;
using Microsoft.EntityFrameworkCore;
using F = LaserWorks.Reporting.Core.ReportFilterKind;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Reporting.Core;

/// <summary>All business reports. Each builds a <see cref="ReportTable"/> from live data through the application services.</summary>
public sealed partial class ReportCatalog
{
    private readonly IAppDbFactory _f;
    private readonly CustomerService _customers;
    private readonly RequestService _requests;
    private readonly QuotationService _quotes;
    private readonly JobService _jobs;
    private readonly JobCostingService _costing;
    private readonly ProductionService _production;
    private readonly InventoryService _inventory;
    private readonly MaterialService _materials;
    private readonly MachineService _machines;
    private readonly PurchaseService _purchases;
    private readonly SalesService _sales;
    private readonly ExpenseService _expenses;
    private readonly AccountingService _accounting;
    private readonly PermissionService _permissions;
    private readonly IClock _clock;
    private readonly JobComponentService _components;
    private const int All = 100_000;

    public ReportCatalog(IAppDbFactory f, CustomerService customers, RequestService requests, QuotationService quotes, JobService jobs, JobCostingService costing,
        ProductionService production, InventoryService inventory, MaterialService materials, MachineService machines, PurchaseService purchases, SalesService sales,
        ExpenseService expenses, AccountingService accounting, PermissionService permissions, IClock clock, JobComponentService components)
    {
        _f = f; _customers = customers; _requests = requests; _quotes = quotes; _jobs = jobs; _costing = costing; _production = production; _inventory = inventory;
        _materials = materials; _machines = machines; _purchases = purchases; _sales = sales; _expenses = expenses; _accounting = accounting; _permissions = permissions; _clock = clock; _components = components;
        Reports = Build();
    }

    public IReadOnlyList<ReportDefinition> Reports { get; }

    public ReportDefinition Get(string key) => Reports.First(r => r.Key == key);

    private static Loc L => Loc.Instance;
    private static PageRequest AllRows => new(PageSize: All);

    public async Task<ReportTable> RunAsync(string key, ReportFilter filter)
    {
        _permissions.Demand(AppModule.Reports, Permission.View);
        var def = Get(key);
        if (def.Required.HasFlag(F.Customer) && filter.CustomerId == null) throw new DomainException("Err.Required", L["Filter.Customer"]);
        if (def.Required.HasFlag(F.Job) && filter.JobId == null) throw new DomainException("Err.Required", L["Filter.Job"]);
        if (def.Required.HasFlag(F.Account) && filter.AccountId == null) throw new DomainException("Err.Required", L["Filter.Account"]);
        var t = await def.Build(filter);
        t.Title = L[def.TitleKey];
        if (def.Filters.HasFlag(F.DateRange)) t.Parameters.Insert(0, $"{L["Filter.From"]}: {L.Date(filter.Range.From)}   {L["Filter.To"]}: {L.Date(filter.Range.To)}");
        if (def.Filters.HasFlag(F.AsOfDate)) t.Parameters.Insert(0, $"{L["Filter.AsOf"]}: {L.Date(filter.AsOf)}");
        return t;
    }

    private static List<(string, string)> StatusOf<T>() where T : struct, Enum => Enum.GetValues<T>().Select(v => (v.ToString(), $"Enum.{typeof(T).Name}.{v}")).ToList();

    private static T? ParseStatus<T>(string? s) where T : struct, Enum => Enum.TryParse<T>(s, out var v) ? v : null;

    private List<ReportDefinition> Build() => new()
    {
        // ---------------------------------------------------------------- customers & sales pipeline
        new("CustomerList", "Rpt.CustomerList", "RptCat.Customers", F.Status, CustomerList, new List<(string, string)> { ("Active", "Common.Active"), ("Inactive", "Common.Inactive") }),
        new("CustomerStatement", "Rpt.CustomerStatement", "RptCat.Customers", F.DateRange | F.Customer, CustomerStatement, Required: F.Customer),
        new("CustomerBalance", "Rpt.CustomerBalance", "RptCat.Customers", F.AsOfDate, CustomerBalance),
        new("CustomerRequests", "Rpt.CustomerRequests", "RptCat.Customers", F.DateRange | F.Customer | F.Status, CustomerRequests, StatusOf<RequestStatus>()),
        new("PendingQuotations", "Rpt.PendingQuotations", "RptCat.Customers", F.Customer, PendingQuotations),
        new("QuotationRegister", "Rpt.QuotationRegister", "RptCat.Customers", F.DateRange | F.Customer | F.Status, QuotationRegister, StatusOf<QuotationStatus>()),
        // ---------------------------------------------------------------- jobs & costing
        new("JobRegister", "Rpt.JobRegister", "RptCat.Jobs", F.DateRange | F.Customer | F.Machine | F.Status, JobRegister, StatusOf<JobStatus>()),
        new("JobStatus", "Rpt.JobStatus", "RptCat.Jobs", F.Customer | F.Machine, JobStatusReport),
        new("JobCostSheet", "Rpt.JobCostSheet", "RptCat.Jobs", F.Job, JobCostSheet, Required: F.Job),
        new("EstimatedVsActual", "Rpt.EstimatedVsActual", "RptCat.Jobs", F.DateRange | F.Job | F.Customer, EstimatedVsActual),
        new("JobComponents", "Rpt.JobComponents", "RptCat.Jobs", F.DateRange | F.Job | F.Customer | F.Status, JobComponentsReport, StatusOf<ComponentCategory>()),
        new("JobCostVariance", "Rpt.JobCostVariance", "RptCat.Jobs", F.DateRange | F.Customer, JobCostVariance),
        new("JobProfitability", "Rpt.JobProfitability", "RptCat.Profit", F.DateRange | F.Customer | F.Status, JobProfitability, new List<(string, string)> { ("Completed", "Filter.CompletedOnly"), ("Flagged", "Filter.FlaggedOnly") }),
        new("CustomerProfitability", "Rpt.CustomerProfitability", "RptCat.Profit", F.DateRange, CustomerProfitability),
        new("MachineProfitability", "Rpt.MachineProfitability", "RptCat.Profit", F.DateRange, MachineProfitability),
        new("GrossProfit", "Rpt.GrossProfit", "RptCat.Profit", F.DateRange, GrossProfit),
        new("GrossMargin", "Rpt.GrossMargin", "RptCat.Profit", F.DateRange | F.Customer, GrossMargin),
        // ---------------------------------------------------------------- production
        new("MachineUtilization", "Rpt.MachineUtilization", "RptCat.Production", F.DateRange, MachineUtilization),
        new("MachineCost", "Rpt.MachineCost", "RptCat.Production", F.DateRange, MachineCost),
        new("ProductionSummary", "Rpt.ProductionSummary", "RptCat.Production", F.DateRange | F.Machine, ProductionSummary),
        new("ScrapAnalysis", "Rpt.ScrapAnalysis", "RptCat.Production", F.DateRange | F.Status, ScrapAnalysis, new List<(string, string)> { ("Job", "Group.Job"), ("Material", "Group.Material"), ("Machine", "Group.Machine"), ("Reason", "Group.Reason"), ("Detail", "Group.Detail") }),
        new("ReworkAnalysis", "Rpt.ReworkAnalysis", "RptCat.Production", F.DateRange, ReworkAnalysis),
        new("QualitySummary", "Rpt.QualitySummary", "RptCat.Production", F.DateRange | F.Status, QualitySummary, StatusOf<QualityStatus>()),
        // ---------------------------------------------------------------- materials & inventory
        new("MaterialConsumption", "Rpt.MaterialConsumption", "RptCat.Inventory", F.DateRange | F.Material, MaterialConsumption),
        new("MaterialUtilization", "Rpt.MaterialUtilization", "RptCat.Inventory", F.DateRange | F.Material, MaterialUtilization),
        new("MaterialCostAnalysis", "Rpt.MaterialCostAnalysis", "RptCat.Inventory", F.DateRange | F.Material, MaterialCostAnalysis),
        new("RemnantInventory", "Rpt.RemnantInventory", "RptCat.Inventory", F.Material | F.Status, RemnantInventory, StatusOf<RemnantStatus>()),
        new("InventoryValuation", "Rpt.InventoryValuation", "RptCat.Inventory", F.None, InventoryValuation),
        new("StockMovement", "Rpt.StockMovement", "RptCat.Inventory", F.DateRange | F.Material | F.Job | F.Status, StockMovement, StatusOf<InventoryTxType>()),
        new("LowStock", "Rpt.LowStock", "RptCat.Inventory", F.None, LowStock),
        // ---------------------------------------------------------------- purchasing, sales, expenses
        new("PurchaseRegister", "Rpt.PurchaseRegister", "RptCat.Trade", F.DateRange | F.Supplier, PurchaseRegister),
        new("SalesRegister", "Rpt.SalesRegister", "RptCat.Trade", F.DateRange | F.Customer, SalesRegister),
        new("SalesReturn", "Rpt.SalesReturn", "RptCat.Trade", F.DateRange | F.Customer, SalesReturns),
        new("ExpenseAnalysis", "Rpt.ExpenseAnalysis", "RptCat.Trade", F.DateRange | F.Machine | F.Job, ExpenseAnalysis),
        new("AccountsReceivable", "Rpt.AccountsReceivable", "RptCat.Finance", F.AsOfDate | F.Customer, AccountsReceivable),
        new("AccountsPayable", "Rpt.AccountsPayable", "RptCat.Finance", F.AsOfDate | F.Supplier, AccountsPayable),
        // ---------------------------------------------------------------- accounting
        new("TrialBalance", "Rpt.TrialBalance", "RptCat.Finance", F.DateRange, TrialBalance),
        new("IncomeStatement", "Rpt.IncomeStatement", "RptCat.Finance", F.DateRange, IncomeStatement),
        new("BalanceSheet", "Rpt.BalanceSheet", "RptCat.Finance", F.AsOfDate, BalanceSheet),
        new("CashFlow", "Rpt.CashFlow", "RptCat.Finance", F.DateRange, CashFlow),
        new("GeneralLedger", "Rpt.GeneralLedger", "RptCat.Finance", F.DateRange | F.Account | F.Customer | F.Job, GeneralLedger, Required: F.Account),
    };

    // ============================================================== customers
    private async Task<ReportTable> CustomerList(ReportFilter f)
    {
        bool? active = f.Status == "Active" ? true : f.Status == "Inactive" ? false : null;
        var rows = (await _customers.ListAsync(AllRows, active)).Items;
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("name", "Col.Name", width: 2f).Col("phone", "Col.Phone").Col("email", "Col.Email", width: 1.5f)
            .Col("tax", "Col.TaxNumber").Col("limit", "Col.CreditLimit", K.Money).Col("terms", "Col.PaymentTerms", K.Integer, 0.6f).Col("balance", "Col.Balance", K.Money, total: true).Col("active", "Col.Active", width: 0.5f);
        foreach (var c in rows)
            t.AddRow(c.CreditLimit > 0 && c.Balance > c.CreditLimit ? RowStyle.Warning : RowStyle.Normal, "Customer", c.Id, c.Code, c.Name, c.Phone, c.Email, c.TaxNumber, c.CreditLimit, c.PaymentTermsDays, c.Balance, c.IsActive);
        return t;
    }

    private async Task<ReportTable> CustomerStatement(ReportFilter f)
    {
        var customer = await _customers.GetAsync(f.CustomerId!.Value) ?? throw new DomainException("Err.NotFound");
        var ar = (await _accounting.PostableAccountsAsync()).First(a => a.SystemKey == SystemAccounts.AR);
        var ledger = await _accounting.GeneralLedgerAsync(ar.Id, f.Range, customerId: customer.Id);
        var t = new ReportTable().Col("date", "Col.Date", K.Date, 0.8f).Col("entry", "Col.Entry", width: 0.9f).Col("doc", "Col.Document").Col("desc", "Col.Description", width: 2.5f)
            .Col("debit", "Col.Debit", K.Money, total: true).Col("credit", "Col.Credit", K.Money, total: true).Col("balance", "Col.Balance", K.Money);
        t.Parameters.Add($"{L["Filter.Customer"]}: {customer.Code} - {customer.Name}");
        foreach (var l in ledger)
            t.AddRow(l.EntryId == 0 ? RowStyle.Subtotal : RowStyle.Normal, l.SourceType, null, l.Date, l.EntryNumber, l.SourceNumber, l.EntryId == 0 ? L["Report.OpeningBalance"] : l.Description, l.Debit, l.Credit, l.Balance);
        return t;
    }

    private async Task<ReportTable> CustomerBalance(ReportFilter f)
    {
        await using var db = _f.Create();
        var asOf = f.AsOf.Date.AddDays(1);
        var arId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AR)).Id;
        var balances = await db.JournalLines.AsNoTracking().Where(l => l.AccountId == arId && l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date < asOf && l.CustomerId != null)
            .GroupBy(l => l.CustomerId).Select(g => new { CustomerId = g.Key, Balance = g.Sum(x => x.Debit - x.Credit) }).ToListAsync();
        var invoices = await OpenInvoicesAsOf(db, f.AsOf, null);
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("name", "Col.Customer", width: 2f).Col("b0", "Col.Aging0_30", K.Money, total: true).Col("b31", "Col.Aging31_60", K.Money, total: true)
            .Col("b61", "Col.Aging61_90", K.Money, total: true).Col("b90", "Col.Aging90", K.Money, total: true).Col("unalloc", "Col.Unallocated", K.Money, total: true).Col("balance", "Col.Balance", K.Money, total: true).Col("limit", "Col.CreditLimit", K.Money);
        foreach (var b in balances.Where(b => b.Balance != 0).OrderByDescending(b => b.Balance))
        {
            var c = customers[b.CustomerId!.Value];
            var mine = invoices.Where(i => i.CustomerId == c.Id).ToList();
            decimal Bucket(int from, int to) => mine.Where(i => i.Days >= from && i.Days <= to).Sum(i => i.Open);
            var aged = mine.Sum(i => i.Open);
            t.AddRow(c.CreditLimit > 0 && b.Balance > c.CreditLimit ? RowStyle.Warning : RowStyle.Normal, "Customer", c.Id, c.Code, c.Name, Bucket(int.MinValue, 30), Bucket(31, 60), Bucket(61, 90), Bucket(91, int.MaxValue), b.Balance - aged, b.Balance, c.CreditLimit);
        }
        return t;
    }

    private sealed record OpenDoc(long Id, string Number, long CustomerId, DateTime Date, DateTime DueDate, decimal Total, decimal Open, int Days);

    /// <summary>Open invoices as of a date: total − payments − returns dated up to that date.</summary>
    private static async Task<List<OpenDoc>> OpenInvoicesAsOf(IAppDb db, DateTime asOf, long? customerId)
    {
        var to = asOf.Date.AddDays(1);
        var inv = await db.SalesInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Posted && i.Date < to && (customerId == null || i.CustomerId == customerId))
            .Select(i => new { i.Id, i.Number, i.CustomerId, i.Date, i.DueDate, i.Total }).ToListAsync();
        var pays = await db.CustomerPayments.AsNoTracking().Where(p => p.InvoiceId != null && p.Date < to).GroupBy(p => p.InvoiceId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync();
        var rets = await db.SalesReturns.AsNoTracking().Where(r => r.Status == DocumentStatus.Posted && r.Date < to && r.RefundMethod == PaymentMethod.OnCredit).GroupBy(r => r.InvoiceId).Select(g => new { g.Key, Sum = g.Sum(x => x.Total) }).ToListAsync();
        return inv.Select(i =>
        {
            var open = i.Total - (pays.FirstOrDefault(p => p.Key == i.Id)?.Sum ?? 0) - (rets.FirstOrDefault(r => r.Key == i.Id)?.Sum ?? 0);
            return new OpenDoc(i.Id, i.Number, i.CustomerId, i.Date, i.DueDate, i.Total, open, (asOf.Date - i.DueDate.Date).Days);
        }).Where(x => x.Open != 0).ToList();
    }

    private async Task<ReportTable> CustomerRequests(ReportFilter f)
    {
        var rows = (await _requests.ListAsync(AllRows, ParseStatus<RequestStatus>(f.Status), f.CustomerId, f.Range)).Items;
        var t = new ReportTable().Col("no", "Col.Number", width: 0.9f).Col("date", "Col.Date", K.Date, 0.8f).Col("customer", "Col.Customer", width: 1.6f).Col("desc", "Col.Description", width: 2.5f)
            .Col("items", "Col.RequestedItems", width: 1.6f).Col("qty", "Col.Quantity", K.Number, 0.6f).Col("required", "Col.RequiredDate", K.Date, 0.8f).Col("status", "Col.Status").Col("revs", "Col.Revisions", K.Integer, 0.5f);
        foreach (var r in rows) t.AddRow(RowStyle.Normal, "Request", r.Id, r.Number, r.RequestDate, r.Customer, r.Description, r.Items, r.Quantity, r.RequiredDate, r.Status, r.Revisions);
        return t;
    }

    private async Task<ReportTable> PendingQuotations(ReportFilter f)
    {
        var rows = (await _quotes.ListAsync(AllRows, null, f.CustomerId)).Items.Where(q => q.Status is QuotationStatus.Draft or QuotationStatus.Sent).ToList();
        var today = _clock.Now.Date;
        var t = QuoteColumns();
        foreach (var q in rows) t.AddRow(q.ValidUntil < today.AddDays(3) ? RowStyle.Warning : RowStyle.Normal, "Quotation", q.Id, q.DisplayNumber, q.Date, q.Customer, q.Description, q.EstimatedCost, q.NetAmount, q.Total, q.MarginPercent, q.ValidUntil, q.Status);
        return t;
    }

    private static ReportTable QuoteColumns() => new ReportTable().Col("no", "Col.Number", width: 0.9f).Col("date", "Col.Date", K.Date, 0.8f).Col("customer", "Col.Customer", width: 1.5f).Col("desc", "Col.Description", width: 2.2f)
        .Col("cost", "Col.EstimatedCost", K.Money, total: true).Col("net", "Col.NetAmount", K.Money, total: true).Col("total", "Col.Total", K.Money, total: true).Col("margin", "Col.Margin", K.Percent, 0.6f)
        .Col("valid", "Col.ValidUntil", K.Date, 0.8f).Col("status", "Col.Status", width: 0.8f);

    private async Task<ReportTable> QuotationRegister(ReportFilter f)
    {
        var rows = (await _quotes.ListAsync(AllRows, ParseStatus<QuotationStatus>(f.Status), f.CustomerId, latestOnly: false, range: f.Range)).Items;
        var t = QuoteColumns();
        foreach (var q in rows) t.AddRow(q.Status == QuotationStatus.Rejected ? RowStyle.Warning : RowStyle.Normal, "Quotation", q.Id, q.DisplayNumber, q.Date, q.Customer, q.Description, q.EstimatedCost, q.NetAmount, q.Total, q.MarginPercent, q.ValidUntil, q.Status);
        return t;
    }

    // ============================================================== jobs
    private async Task<ReportTable> JobRegister(ReportFilter f)
    {
        var rows = (await _jobs.ListAsync(AllRows, new JobFilter(ParseStatus<JobStatus>(f.Status), f.CustomerId, Range: f.Range, MachineId: f.MachineId))).Items;
        var t = new ReportTable().Col("no", "Col.Job", width: 0.8f).Col("date", "Col.Date", K.Date, 0.75f).Col("customer", "Col.Customer", width: 1.5f).Col("title", "Col.Description", width: 2.2f)
            .Col("qty", "Col.Quantity", K.Number, 0.5f).Col("due", "Col.DueDate", K.Date, 0.75f).Col("status", "Col.Status", width: 0.8f).Col("est", "Col.EstimatedCost", K.Money, total: true)
            .Col("act", "Col.ActualCost", K.Money, total: true).Col("price", "Col.SellingPrice", K.Money, total: true).Col("margin", "Col.Margin", K.Percent, 0.6f);
        foreach (var j in rows)
            t.AddRow(j.ActualCost > j.SellingPrice && j.ActualCost > 0 ? RowStyle.Negative : RowStyle.Normal, "Job", j.Id, j.Number, j.OrderDate, j.Customer, j.Title, j.Quantity, j.DueDate, j.Status, j.EstimatedCost, j.ActualCost, j.SellingPrice, j.ActualCost == 0 ? (decimal?)null : j.MarginPercent);
        return t;
    }

    private async Task<ReportTable> JobStatusReport(ReportFilter f)
    {
        var rows = (await _jobs.ListAsync(AllRows, new JobFilter(CustomerId: f.CustomerId, OpenOnly: true, MachineId: f.MachineId))).Items;
        var ops = (await _production.ListOperationsAsync(AllRows, openJobsOnly: true)).Items.GroupBy(o => o.JobId).ToDictionary(g => g.Key, g => g.ToList());
        var today = _clock.Now.Date;
        var t = new ReportTable().Col("no", "Col.Job", width: 0.8f).Col("customer", "Col.Customer", width: 1.5f).Col("title", "Col.Description", width: 2.2f).Col("priority", "Col.Priority", width: 0.7f)
            .Col("status", "Col.Status", width: 0.9f).Col("due", "Col.DueDate", K.Date, 0.8f).Col("days", "Col.DaysLeft", K.Integer, 0.6f).Col("ops", "Col.OperationsDone", width: 0.7f)
            .Col("planned", "Col.PlannedHours", K.Number, 0.7f).Col("actual", "Col.ActualHours", K.Number, 0.7f).Col("machine", "Col.Machine", width: 1.2f);
        foreach (var j in rows.OrderBy(j => j.DueDate ?? DateTime.MaxValue))
        {
            var o = ops.GetValueOrDefault(j.Id) ?? new List<OperationRow>();
            var days = j.DueDate.HasValue ? (int?)(j.DueDate.Value.Date - today).Days : null;
            t.AddRow(days < 0 ? RowStyle.Negative : days <= 2 ? RowStyle.Warning : RowStyle.Normal, "Job", j.Id, j.Number, j.Customer, j.Title, j.Priority, j.Status, j.DueDate, days,
                $"{o.Count(x => x.Status == OperationStatus.Done)}/{o.Count}", o.Sum(x => x.PlannedHours), o.Sum(x => x.ActualHours), j.Machine);
        }
        return t;
    }

    private async Task<ReportTable> JobCostSheet(ReportFilter f)
    {
        var sheet = await _costing.CostSheetAsync(f.JobId!.Value);
        var t = new ReportTable().Col("date", "Col.Date", K.Date, 0.8f).Col("component", "Col.Component", width: 1f).Col("source", "Col.Source", width: 0.9f).Col("desc", "Col.Description", width: 2.8f)
            .Col("qty", "Col.Quantity", K.Number, 0.6f).Col("hours", "Col.Hours", K.Number, 0.6f).Col("amount", "Col.Amount", K.Money, total: true).Col("je", "Col.Entry", width: 0.8f);
        t.Parameters.Add($"{L["Col.Job"]}: {sheet.Job.Number} — {sheet.Job.Title}");
        t.Parameters.Add($"{L["Col.Customer"]}: {sheet.Customer}");
        foreach (var e in sheet.Entries) t.Add(e.Date, e.Component, L.Source(e.SourceType), e.Description, e.Quantity == 0 ? null : e.Quantity, e.Hours == 0 ? null : e.Hours, e.Amount, e.JournalNumber);
        t.Notes.Add($"{L["Col.SellingPrice"]}: {L.Money(sheet.Revenue)}   {L["Col.ActualCost"]}: {L.Money(sheet.ActualCost)}   {L["Col.GrossProfit"]}: {L.Money(sheet.GrossProfit)}   {L["Col.Margin"]}: {L.Percent(sheet.MarginPercent)}");
        t.Notes.Add($"{L["Col.EstimatedCost"]}: {L.Money(sheet.Variance.EstimatedTotal)}   {L["Col.Variance"]}: {L.Money(sheet.Variance.Variance)} ({L.Percent(sheet.Variance.VariancePercent)})");
        return t;
    }

    private async Task<ReportTable> EstimatedVsActual(ReportFilter f)
    {
        var t = new ReportTable();
        if (f.JobId is { } jid)
        {
            // one job: estimated vs actual per component line, then the costs that are not on a line
            var sheet = await _costing.CostSheetAsync(jid);
            t.Col("item", "Col.Item", width: 2f).Col("type", "Col.Type", width: 1.3f).Col("planned", "Col.PlannedQty", K.Number, 0.7f).Col("used", "Col.UsedQty", K.Number, 0.7f)
                .Col("unit", "Col.Unit", width: 0.5f).Col("est", "Col.Estimated", K.Money, total: true).Col("act", "Col.Actual", K.Money, total: true).Col("var", "Col.Variance", K.Money, total: true)
                .Col("pct", "Col.VariancePercent", K.Percent, 0.7f).Col("flag", "Col.Assessment", width: 1f);
            t.Parameters.Add($"{L["Col.Job"]}: {sheet.Job.Number} — {sheet.Job.Title}");
            foreach (var c in sheet.Components)
                t.AddRow(c.Variance > 0 ? RowStyle.Warning : c.IsLine ? RowStyle.Normal : RowStyle.Subtotal, null, null,
                    c.IsLine ? $"{c.LineNo}. {c.Item}" : L.Enum(c.Component),
                    c.IsLine ? $"{L.Enum(c.Category!.Value)} · {L.Enum(c.Source!.Value)}" : L["Doc.NotOnLine"],
                    c.IsLine ? c.PlannedQuantity : null, c.IsLine ? c.UsedQuantity : null, c.Unit, c.Estimated, c.Actual, c.Variance, c.VariancePercent,
                    c.Variance > 0 ? L["Var.OverBudget"] : c.Variance < 0 ? L["Var.UnderBudget"] : L["Var.OnBudget"]);
            if (sheet.Variance.MainDriver is { } drv) t.Notes.Add($"{L["Var.MainReason"]}: {L.Enum(drv)}");
            t.Notes.Add(L["Var.Legend"]);
            return t;
        }
        var rows = await _costing.JobProfitabilityAsync(f.Range, completedOnly: true, customerId: f.CustomerId);
        t.Col("job", "Col.Job", width: 0.8f).Col("customer", "Col.Customer", width: 1.6f).Col("est", "Col.Estimated", K.Money, total: true).Col("act", "Col.Actual", K.Money, total: true)
            .Col("var", "Col.Variance", K.Money, total: true).Col("pct", "Col.VariancePercent", K.Percent, 0.7f).Col("matvar", "Col.MaterialVariancePct", K.Percent, 0.8f).Col("machvar", "Col.MachineVariancePct", K.Percent, 0.8f).Col("driver", "Col.MainReason", width: 1f);
        foreach (var r in rows)
        {
            var driver = new[] { (CostComponent.Material, r.MaterialActual - r.MaterialEstimated), (CostComponent.Machine, r.MachineActual - r.MachineEstimated), (CostComponent.Scrap, r.ScrapCost), (CostComponent.Rework, r.ReworkCost) }
                .OrderByDescending(x => Math.Abs(x.Item2)).First();
            t.AddRow(r.CostVariance > 0 ? RowStyle.Warning : RowStyle.Normal, "Job", r.JobId, r.JobNumber, r.Customer, r.EstimatedCost, r.ActualCost, r.CostVariance, r.CostVariancePercent, r.MaterialVariancePercent, r.MachineVariancePercent, driver.Item2 == 0 ? "" : L.Enum(driver.Item1));
        }
        return t;
    }

    /// <summary>Every job component line in the period: planned vs used quantity and estimated vs actual cost.</summary>
    private async Task<ReportTable> JobComponentsReport(ReportFilter f)
    {
        await using var db = _f.Create();
        var category = ParseStatus<ComponentCategory>(f.Status);
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.Status != JobStatus.Cancelled && (f.JobId != null ? j.Id == f.JobId : j.OrderDate >= f.Range.From.Date && j.OrderDate < f.Range.ToExclusive)
                && (f.CustomerId == null || j.CustomerId == f.CustomerId))
            .OrderBy(j => j.Number).Select(j => new { j.Id, j.Number, Customer = j.Customer!.Name }).ToListAsync();
        var t = new ReportTable().Col("job", "Col.Job", width: 0.8f).Col("customer", "Col.Customer", width: 1.3f).Col("item", "Col.Item", width: 2f).Col("type", "Col.Type", width: 1f)
            .Col("source", "Col.Source", width: 0.9f).Col("planned", "Col.PlannedQty", K.Number, 0.7f).Col("used", "Col.UsedQty", K.Number, 0.7f).Col("unit", "Col.Unit", width: 0.5f)
            .Col("est", "Col.Estimated", K.Money, total: true).Col("act", "Col.Actual", K.Money, total: true).Col("var", "Col.Variance", K.Money, total: true);
        foreach (var j in jobs)
            foreach (var l in await _components.ListAsync(j.Id))
            {
                if (category != null && l.Category != category) continue;
                t.AddRow(l.Variance > 0 ? RowStyle.Warning : RowStyle.Normal, "Job", j.Id, j.Number, j.Customer, l.Display, L.Enum(l.Category), L.Enum(l.Source),
                    l.PlannedQuantity, l.UsedQuantity, l.Unit, l.EstimatedCost, l.ActualCost, l.Variance);
            }
        return t;
    }

    private async Task<ReportTable> JobCostVariance(ReportFilter f)
    {
        await using var db = _f.Create();
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.Status >= JobStatus.QualityCheck && j.Status != JobStatus.Cancelled && j.OrderDate >= f.Range.From.Date && j.OrderDate < f.Range.ToExclusive && (f.CustomerId == null || j.CustomerId == f.CustomerId))
            .OrderBy(j => j.Number).Select(j => new { j.Id, j.Number, j.EstimateId, j.EstimatedCost }).ToListAsync();
        var t = new ReportTable().Col("job", "Col.Job", width: 0.8f);
        var comps = new[] { CostComponent.Material, CostComponent.PurchasedComponents, CostComponent.Machine, CostComponent.Labor, CostComponent.Scrap, CostComponent.Rework, CostComponent.Overhead };
        foreach (var c in comps) t.Col(c.ToString(), $"Enum.CostComponent.{c}", K.Money, total: true);
        t.Col("other", "Col.Other", K.Money, total: true).Col("total", "Col.TotalVariance", K.Money, total: true);
        foreach (var j in jobs)
        {
            var v = await _costing.VarianceAsync(j.Id);
            var cells = new List<object?> { j.Number };
            foreach (var c in comps) cells.Add(v.Lines.Where(l => l.Component == c).Sum(l => l.Variance));
            cells.Add(v.Lines.Where(l => !comps.Contains(l.Component)).Sum(l => l.Variance));
            cells.Add(v.Variance);
            t.AddRow(v.Variance > 0 ? RowStyle.Warning : RowStyle.Normal, "Job", j.Id, cells.ToArray());
        }
        t.Notes.Add(L["Var.Legend"]);
        return t;
    }

    private async Task<ReportTable> JobProfitability(ReportFilter f)
    {
        var rows = await _costing.JobProfitabilityAsync(f.Range, completedOnly: f.Status == "Completed", customerId: f.CustomerId);
        var t = new ReportTable().Col("job", "Col.Job", width: 0.8f).Col("customer", "Col.Customer", width: 1.5f).Col("status", "Col.Status", width: 0.8f).Col("revenue", "Col.Revenue", K.Money, total: true)
            .Col("cost", "Col.ActualCost", K.Money, total: true).Col("gp", "Col.GrossProfit", K.Money, total: true).Col("margin", "Col.Margin", K.Percent, 0.6f).Col("scrap", "Col.ScrapPct", K.Percent, 0.6f).Col("flags", "Col.Flags", width: 1.8f);
        foreach (var r in rows)
        {
            var fl = _costing.Flags(r);
            if (f.Status == "Flagged" && !fl.Any) continue;
            var flags = string.Join(", ", new[] { fl.NegativeMargin ? L["Flag.NegativeMargin"] : null, fl.LowMargin ? L["Flag.LowMargin"] : null, fl.HighMaterialVariance ? L["Flag.HighMaterialVariance"] : null,
                fl.HighScrap ? L["Flag.HighScrap"] : null, fl.HighMachineVariance ? L["Flag.HighMachineVariance"] : null }.Where(x => x != null));
            t.AddRow(fl.NegativeMargin ? RowStyle.Negative : fl.Any ? RowStyle.Warning : RowStyle.Normal, "Job", r.JobId, r.JobNumber, r.Customer, r.Status, r.Revenue, r.ActualCost, r.GrossProfit, r.ActualCost == 0 ? null : r.MarginPercent, r.ScrapPercent, flags);
        }
        return t;
    }

    private static ReportTable GroupTable(string keyHeader, bool hours = false, bool qty = false)
    {
        var t = new ReportTable().Col("key", "Col.Code", width: 0.8f).Col("name", keyHeader, width: 2f).Col("jobs", "Col.Jobs", K.Integer, 0.6f);
        if (hours) t.Col("hours", "Col.MachineHours", K.Number, 0.8f).Col("absorbed", "Col.MachineCostAbsorbed", K.Money, total: true);
        if (qty) t.Col("qty", "Col.QuantityUsed", K.Number, 0.8f);
        return t.Col("revenue", "Col.Revenue", K.Money, total: true).Col("cost", "Col.Cost", K.Money, total: true).Col("gp", "Col.GrossProfit", K.Money, total: true).Col("margin", "Col.Margin", K.Percent, 0.7f);
    }

    private async Task<ReportTable> CustomerProfitability(ReportFilter f)
    {
        var t = GroupTable("Col.Customer");
        foreach (var r in await _costing.ByCustomerAsync(f.Range)) t.AddRow(r.GrossProfit < 0 ? RowStyle.Negative : RowStyle.Normal, null, null, r.Key, r.Name, r.Jobs, r.Revenue, r.Cost, r.GrossProfit, r.MarginPercent);
        return t;
    }

    private async Task<ReportTable> MachineProfitability(ReportFilter f)
    {
        var t = GroupTable("Col.Machine", hours: true);
        foreach (var r in await _costing.ByMachineAsync(f.Range)) t.AddRow(r.GrossProfit < 0 ? RowStyle.Negative : RowStyle.Normal, null, null, r.Key, r.Name, r.Jobs, r.Hours, r.Quantity, r.Revenue, r.Cost, r.GrossProfit, r.MarginPercent);
        t.Notes.Add(L["Rpt.MachineProfitability.Note"]);
        return t;
    }

    private async Task<ReportTable> GrossProfit(ReportFilter f)
    {
        var t = new ReportTable().Col("month", "Col.Month", width: 1f).Col("revenue", "Col.NetRevenue", K.Money, total: true).Col("cogs", "Col.Cogs", K.Money, total: true).Col("gp", "Col.GrossProfit", K.Money, total: true).Col("margin", "Col.Margin", K.Percent);
        foreach (var r in await _costing.ByMonthAsync(f.Range)) t.AddRow(r.GrossProfit < 0 ? RowStyle.Negative : RowStyle.Normal, null, null, r.Key, r.Revenue, r.Cost, r.GrossProfit, r.MarginPercent);
        return t;
    }

    private async Task<ReportTable> GrossMargin(ReportFilter f)
    {
        var rows = (await _sales.ListInvoicesAsync(AllRows, DocumentStatus.Posted, f.CustomerId, f.Range)).Items;
        var t = new ReportTable().Col("no", "Col.Invoice", width: 0.9f).Col("date", "Col.Date", K.Date, 0.8f).Col("customer", "Col.Customer", width: 1.6f).Col("job", "Col.Job", width: 0.8f)
            .Col("revenue", "Col.NetRevenue", K.Money, total: true).Col("cogs", "Col.Cogs", K.Money, total: true).Col("gp", "Col.GrossProfit", K.Money, total: true).Col("margin", "Col.Margin", K.Percent, 0.7f);
        foreach (var i in rows.OrderBy(i => Money.Percent(i.NetRevenue - i.CogsAmount, i.NetRevenue)))
        {
            var gp = i.NetRevenue - i.CogsAmount;
            t.AddRow(gp < 0 ? RowStyle.Negative : RowStyle.Normal, "Invoice", i.Id, i.Number, i.Date, i.Customer, i.JobNumber, i.NetRevenue, i.CogsAmount, gp, Money.Percent(gp, i.NetRevenue));
        }
        t.Notes.Add(L["Rpt.GrossMargin.Note"]);
        return t;
    }

    // ============================================================== production
    private async Task<ReportTable> MachineUtilization(ReportFilter f)
    {
        await using var db = _f.Create();
        var days = (decimal)(f.Range.ToExclusive - f.Range.From.Date).TotalDays;
        var hours = await db.JobOperations.AsNoTracking().Where(o => o.MachineId != null && o.CostPosted && o.EndTime >= f.Range.From.Date && o.EndTime < f.Range.ToExclusive)
            .GroupBy(o => o.MachineId).Select(g => new { g.Key, Hours = g.Sum(x => x.MachineHours), Planned = g.Sum(x => x.PlannedHours), Ops = g.Count(), Jobs = g.Select(x => x.JobId).Distinct().Count() }).ToListAsync();
        var machines = await db.Machines.AsNoTracking().OrderBy(m => m.Code).ToListAsync();
        var t = new ReportTable().Col("code", "Col.Code", width: 0.7f).Col("name", "Col.Machine", width: 2f).Col("available", "Col.AvailableHours", K.Number).Col("planned", "Col.PlannedHours", K.Number)
            .Col("used", "Col.MachineHours", K.Number, total: true).Col("util", "Col.Utilization", K.Percent).Col("ops", "Col.Operations", K.Integer, 0.7f).Col("jobs", "Col.Jobs", K.Integer, 0.6f);
        foreach (var m in machines)
        {
            var h = hours.FirstOrDefault(x => x.Key == m.Id);
            var available = Math.Round(m.AnnualWorkingHours * days / 365m, 1);
            var used = h?.Hours ?? 0;
            t.AddRow(RowStyle.Normal, "Machine", m.Id, m.Code, m.Name, available, h?.Planned ?? 0, used, Money.Percent(used, available), h?.Ops ?? 0, h?.Jobs ?? 0);
        }
        return t;
    }

    private async Task<ReportTable> MachineCost(ReportFilter f)
    {
        await using var db = _f.Create();
        var machines = await db.Machines.AsNoTracking().OrderBy(m => m.Code).ToListAsync();
        var absorbed = await db.JobCostEntries.AsNoTracking().Where(e => e.MachineId != null && e.EmployeeId == null && e.Date >= f.Range.From.Date && e.Date < f.Range.ToExclusive
                                                                          && (e.Component == CostComponent.Machine || e.Component == CostComponent.Maintenance || e.Component == CostComponent.Rework))
            .GroupBy(e => e.MachineId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount), Hours = g.Where(x => x.Component != CostComponent.Maintenance).Sum(x => x.Hours) }).ToListAsync();
        var maintExp = await db.Expenses.AsNoTracking().Where(e => e.MachineId != null && e.Status == DocumentStatus.Posted && e.Date >= f.Range.From.Date && e.Date < f.Range.ToExclusive)
            .GroupBy(e => e.MachineId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync();
        var t = new ReportTable().Col("name", "Col.Machine", width: 1.8f).Col("dep", "Col.DepreciationPerHour", K.Money).Col("elec", "Col.ElectricityPerHour", K.Money).Col("maint", "Col.MaintenancePerHour", K.Money)
            .Col("op", "Col.OperatorPerHour", K.Money).Col("oh", "Col.OverheadPerHour", K.Money).Col("rate", "Col.HourlyRate", K.Money).Col("hours", "Col.MachineHours", K.Number, total: true)
            .Col("absorbed", "Col.MachineCostAbsorbed", K.Money, total: true).Col("direct", "Col.DirectMachineExpenses", K.Money, total: true);
        foreach (var m in machines)
        {
            var r = m.CalculateRate();
            var a = absorbed.FirstOrDefault(x => x.Key == m.Id);
            t.AddRow(RowStyle.Normal, "Machine", m.Id, m.Name + (r.IsOverridden ? " *" : ""), r.DepreciationPerHour, r.ElectricityPerHour, r.MaintenancePerHour, r.OperatorPerHour, r.OverheadPerHour, r.EffectiveRate,
                a?.Hours ?? 0, a?.Sum ?? 0, maintExp.FirstOrDefault(x => x.Key == m.Id)?.Sum ?? 0);
        }
        t.Notes.Add(L["Rpt.MachineCost.Note"]);
        return t;
    }

    private async Task<ReportTable> ProductionSummary(ReportFilter f)
    {
        await using var db = _f.Create();
        var ops = await db.JobOperations.AsNoTracking().Where(o => o.Status == OperationStatus.Done && o.EndTime >= f.Range.From.Date && o.EndTime < f.Range.ToExclusive && (f.MachineId == null || o.MachineId == f.MachineId))
            .GroupBy(o => o.OperationType).Select(g => new
            {
                g.Key, Count = g.Count(), Jobs = g.Select(x => x.JobId).Distinct().Count(), Planned = g.Sum(x => x.PlannedHours), Actual = g.Sum(x => x.ActualHours), Machine = g.Sum(x => x.MachineHours),
                Qty = g.Sum(x => x.Quantity), Scrap = g.Sum(x => x.ScrapQuantity), Rework = g.Sum(x => x.ReworkQuantity), Cost = g.Sum(x => x.MachineCost + x.MaintenanceCost + x.LaborCost)
            }).ToListAsync();
        var t = new ReportTable().Col("op", "Col.Operation", width: 1.3f).Col("count", "Col.Operations", K.Integer, 0.7f).Col("jobs", "Col.Jobs", K.Integer, 0.6f).Col("planned", "Col.PlannedHours", K.Number, total: true)
            .Col("actual", "Col.ActualHours", K.Number, total: true).Col("eff", "Col.Efficiency", K.Percent, 0.7f).Col("machine", "Col.MachineHours", K.Number, total: true).Col("qty", "Col.Quantity", K.Number, total: true)
            .Col("scrap", "Col.ScrapQty", K.Number, total: true).Col("rework", "Col.ReworkQty", K.Number, total: true).Col("cost", "Col.Cost", K.Money, total: true);
        foreach (var o in ops.OrderBy(o => o.Key))
            t.AddRow(o.Actual > o.Planned * 1.15m ? RowStyle.Warning : RowStyle.Normal, null, null, o.Key, o.Count, o.Jobs, o.Planned, o.Actual, Money.Percent(o.Planned, o.Actual), o.Machine, o.Qty, o.Scrap, o.Rework, o.Cost);
        t.Notes.Add(L["Rpt.ProductionSummary.Note"]);
        return t;
    }

    private async Task<ReportTable> ScrapAnalysis(ReportFilter f)
    {
        var rows = (await _production.ListScrapAsync(AllRows, range: f.Range)).Items.Where(r => r.Type != ScrapType.Rework).ToList();
        var group = f.Status ?? "Reason";
        if (group == "Detail")
        {
            var d = new ReportTable().Col("no", "Col.Number", width: 0.8f).Col("date", "Col.Date", K.Date, 0.8f).Col("type", "Col.Type").Col("job", "Col.Job", width: 0.8f).Col("material", "Col.Material", width: 1.5f)
                .Col("machine", "Col.Machine", width: 1.2f).Col("qty", "Col.Quantity", K.Number, 0.6f).Col("reason", "Col.Reason", width: 2f).Col("cost", "Col.Cost", K.Money, total: true);
            foreach (var r in rows) d.AddRow(r.Type == ScrapType.AbnormalScrap ? RowStyle.Warning : RowStyle.Normal, "Job", r.JobId, r.Number, r.Date, r.Type, r.JobNumber, r.Material, r.Machine, r.Quantity, r.Reason, r.Cost);
            return d;
        }
        Func<ScrapRow, string> key = group switch
        {
            "Job" => r => r.JobNumber,
            "Material" => r => r.Material ?? "—",
            "Machine" => r => r.Machine ?? "—",
            _ => r => r.Reason
        };
        var t = new ReportTable().Col("group", group switch { "Job" => "Col.Job", "Material" => "Col.Material", "Machine" => "Col.Machine", _ => "Col.Reason" }, width: 2.2f)
            .Col("count", "Col.Records", K.Integer, 0.6f).Col("normal", "Enum.ScrapType.NormalScrap", K.Money, total: true).Col("abnormal", "Enum.ScrapType.AbnormalScrap", K.Money, total: true)
            .Col("other", "Col.DamageWaste", K.Money, total: true).Col("qty", "Col.Quantity", K.Number, total: true).Col("cost", "Col.Cost", K.Money, total: true).Col("share", "Col.Share", K.Percent, 0.6f);
        var total = rows.Sum(r => r.Cost);
        foreach (var g in rows.GroupBy(key).OrderByDescending(g => g.Sum(x => x.Cost)))
            t.Add(g.Key, g.Count(), g.Where(x => x.Type == ScrapType.NormalScrap).Sum(x => x.Cost), g.Where(x => x.Type == ScrapType.AbnormalScrap).Sum(x => x.Cost),
                g.Where(x => x.Type is ScrapType.Damage or ScrapType.MaterialWaste).Sum(x => x.Cost), g.Sum(x => x.Quantity), g.Sum(x => x.Cost), Money.Percent(g.Sum(x => x.Cost), total));
        return t;
    }

    private async Task<ReportTable> ReworkAnalysis(ReportFilter f)
    {
        var rows = (await _production.ListScrapAsync(AllRows, type: ScrapType.Rework, range: f.Range)).Items;
        var ops = (await _production.ListOperationsAsync(AllRows)).Items.Where(o => o.IsRework && o.EndTime >= f.Range.From.Date && o.EndTime < f.Range.ToExclusive).ToList();
        var t = new ReportTable().Col("no", "Col.Number", width: 0.8f).Col("date", "Col.Date", K.Date, 0.8f).Col("job", "Col.Job", width: 0.8f).Col("machine", "Col.Machine", width: 1.3f)
            .Col("qty", "Col.Quantity", K.Number, total: true).Col("hours", "Col.Hours", K.Number, total: true).Col("reason", "Col.Reason", width: 2.2f).Col("cost", "Col.Cost", K.Money, total: true);
        foreach (var r in rows) t.AddRow(RowStyle.Normal, "Job", r.JobId, r.Number, r.Date, r.JobNumber, r.Machine, r.Quantity, r.Hours, r.Reason, r.Cost);
        foreach (var o in ops) t.AddRow(RowStyle.Normal, "Job", o.JobId, L["Col.Operation"], o.EndTime, o.JobNumber, o.Machine, o.Quantity, o.ActualHours, $"{L.Enum(o.OperationType)} ({L["Common.ReworkOperation"]})", o.TotalCost);
        return t;
    }

    private async Task<ReportTable> QualitySummary(ReportFilter f)
    {
        var rows = (await _production.ListQualityAsync(AllRows, status: ParseStatus<QualityStatus>(f.Status), range: f.Range)).Items;
        var t = new ReportTable().Col("date", "Col.Date", K.Date, 0.8f).Col("job", "Col.Job", width: 0.8f).Col("customer", "Col.Customer", width: 1.5f).Col("inspector", "Col.Inspector", width: 1.2f)
            .Col("produced", "Col.Produced", K.Number, total: true).Col("accepted", "Col.Accepted", K.Number, total: true).Col("rejected", "Col.Rejected", K.Number, total: true)
            .Col("rework", "Col.ReworkQty", K.Number, total: true).Col("rate", "Col.AcceptanceRate", K.Percent, 0.7f).Col("status", "Col.Status", width: 0.9f).Col("notes", "Col.Notes", width: 1.6f);
        foreach (var q in rows)
            t.AddRow(q.Status is QualityStatus.Failed ? RowStyle.Negative : q.Status == QualityStatus.ReworkRequired ? RowStyle.Warning : RowStyle.Normal, "Job", q.JobId,
                q.Date, q.JobNumber, q.Customer, q.Inspector, q.QuantityProduced, q.QuantityAccepted, q.QuantityRejected, q.ReworkQuantity, q.AcceptanceRate, q.Status, q.Notes);
        var produced = rows.Sum(r => r.QuantityProduced);
        t.Notes.Add($"{L["Col.AcceptanceRate"]}: {L.Percent(Money.Percent(rows.Sum(r => r.QuantityAccepted), produced))}");
        return t;
    }

    // ============================================================== inventory
    private async Task<ReportTable> MaterialConsumption(ReportFilter f)
    {
        await using var db = _f.Create();
        var tx = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.Date >= f.Range.From.Date && t.Date < f.Range.ToExclusive && (f.MaterialId == null || t.MaterialId == f.MaterialId)
                        && (t.Type == InventoryTxType.MaterialIssue || t.Type == InventoryTxType.MaterialReturn || t.Type == InventoryTxType.RemnantConsumption || t.Type == InventoryTxType.RemnantCreation || t.Type == InventoryTxType.SalesIssue || t.Type == InventoryTxType.Scrap))
            .GroupBy(t => new { t.MaterialId, t.Material!.Code, t.Material.Name, t.Material.Kind, Unit = t.Material.Unit!.Code })
            .Select(g => new
            {
                g.Key.MaterialId, g.Key.Code, g.Key.Name, g.Key.Kind, g.Key.Unit,
                Issued = -g.Where(x => x.Type == InventoryTxType.MaterialIssue).Sum(x => x.Quantity),
                Returned = g.Where(x => x.Type == InventoryTxType.MaterialReturn).Sum(x => x.Quantity),
                IssuedValue = -g.Where(x => x.Type == InventoryTxType.MaterialIssue || x.Type == InventoryTxType.MaterialReturn).Sum(x => x.TotalCost),
                RemnantUsed = -g.Where(x => x.Type == InventoryTxType.RemnantConsumption).Sum(x => x.TotalCost),
                RemnantCreated = g.Where(x => x.Type == InventoryTxType.RemnantCreation && x.JobId != null).Sum(x => x.TotalCost),
                Sold = -g.Where(x => x.Type == InventoryTxType.SalesIssue).Sum(x => x.Quantity),
                Scrapped = -g.Where(x => x.Type == InventoryTxType.Scrap).Sum(x => x.Quantity),
                Jobs = g.Where(x => x.JobId != null).Select(x => x.JobId).Distinct().Count()
            }).ToListAsync();
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("name", "Col.Material", width: 2f).Col("kind", "Col.Kind", width: 1f).Col("unit", "Col.Unit", width: 0.5f).Col("issued", "Col.Issued", K.Number).Col("returned", "Col.Returned", K.Number)
            .Col("net", "Col.NetConsumed", K.Number).Col("sold", "Col.Sold", K.Number).Col("scrapped", "Col.WrittenOff", K.Number).Col("jobs", "Col.Jobs", K.Integer, 0.5f)
            .Col("value", "Col.MaterialCost", K.Money, total: true).Col("remused", "Col.RemnantsUsed", K.Money, total: true).Col("remmade", "Col.RemnantsCreated", K.Money, total: true);
        foreach (var r in tx.OrderByDescending(x => x.IssuedValue))
            t.AddRow(RowStyle.Normal, "Material", r.MaterialId, r.Code, r.Name, L.Enum(r.Kind), r.Unit, r.Issued, r.Returned, r.Issued - r.Returned, r.Sold, r.Scrapped, r.Jobs, r.IssuedValue, r.RemnantUsed, r.RemnantCreated);
        return t;
    }

    private async Task<ReportTable> MaterialUtilization(ReportFilter f)
    {
        await using var db = _f.Create();
        var lines = await db.EstimateMaterialLines.AsNoTracking()
            .Join(db.CostEstimates, l => l.EstimateId, e => e.Id, (l, e) => new { l, e })
            .Where(x => x.l.SheetBased && x.e.Date >= f.Range.From.Date && x.e.Date < f.Range.ToExclusive && (f.MaterialId == null || x.l.MaterialId == f.MaterialId))
            .Select(x => new { x.l.MaterialId, x.l.Material!.Code, x.l.Material.Name, x.e.Id, x.l.SheetsRequired, x.l.UtilizationPercent, x.l.WasteArea, SheetArea = x.l.SheetLength * x.l.SheetWidth }).ToListAsync();
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.EstimateId != null).Select(j => new { j.Id, j.EstimateId }).ToListAsync();
        var jobByEst = jobs.ToLookup(j => j.EstimateId!.Value, j => j.Id);
        var issued = await db.InventoryTransactions.AsNoTracking().Where(t => t.JobId != null && t.RemnantId == null && (t.Type == InventoryTxType.MaterialIssue || t.Type == InventoryTxType.MaterialReturn))
            .GroupBy(t => new { t.JobId, t.MaterialId }).Select(g => new { g.Key.JobId, g.Key.MaterialId, Qty = -g.Sum(x => x.Quantity) }).ToListAsync();
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("name", "Col.Material", width: 2f).Col("estimates", "Col.Estimates", K.Integer, 0.6f).Col("sheetsEst", "Col.SheetsEstimated", K.Number, total: true)
            .Col("sheetsAct", "Col.SheetsIssued", K.Number, total: true).Col("util", "Col.AvgUtilization", K.Percent).Col("actutil", "Col.ActualUtilization", K.Percent).Col("waste", "Col.WasteArea", K.Number, total: true);
        foreach (var g in lines.GroupBy(x => new { x.MaterialId, x.Code, x.Name }))
        {
            var sheetsEst = g.Sum(x => x.SheetsRequired);
            var used = g.Sum(x => x.SheetsRequired * x.SheetArea * x.UtilizationPercent / 100m);
            var sheetsAct = g.SelectMany(x => jobByEst[x.Id]).Distinct().Sum(jid => issued.Where(i => i.JobId == jid && i.MaterialId == g.Key.MaterialId).Sum(i => i.Qty));
            var area = g.Select(x => x.SheetArea).DefaultIfEmpty(0).Max();
            t.AddRow(RowStyle.Normal, "Material", g.Key.MaterialId, g.Key.Code, g.Key.Name, g.Select(x => x.Id).Distinct().Count(), sheetsEst, sheetsAct,
                Money.Percent(used, sheetsEst * area), sheetsAct > 0 ? Money.Percent(used, sheetsAct * area) : (decimal?)null, g.Sum(x => x.WasteArea));
        }
        t.Notes.Add(L["Rpt.MaterialUtilization.Note"]);
        return t;
    }

    private async Task<ReportTable> MaterialCostAnalysis(ReportFilter f)
    {
        await using var db = _f.Create();
        var receipts = await db.PurchaseReceiptLines.AsNoTracking()
            .Where(l => l.Receipt!.Status == DocumentStatus.Posted && l.Receipt.Date >= f.Range.From.Date && l.Receipt.Date < f.Range.ToExclusive && (f.MaterialId == null || l.MaterialId == f.MaterialId))
            .Select(l => new { l.MaterialId, l.Quantity, l.UnitCost, l.Receipt!.Date }).ToListAsync();
        var mats = await db.Materials.AsNoTracking().Where(m => f.MaterialId == null || m.Id == f.MaterialId).OrderBy(m => m.Code).ToListAsync();
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("name", "Col.Material", width: 2f).Col("qty", "Col.QuantityPurchased", K.Number).Col("value", "Col.PurchaseValue", K.Money, total: true)
            .Col("min", "Col.MinCost", K.Money).Col("max", "Col.MaxCost", K.Money).Col("wavg", "Col.AvgPurchaseCost", K.Money).Col("last", "Col.LastCost", K.Money).Col("avg", "Col.AverageCost", K.Money).Col("change", "Col.PriceChange", K.Percent, 0.7f);
        foreach (var m in mats)
        {
            var r = receipts.Where(x => x.MaterialId == m.Id).OrderBy(x => x.Date).ToList();
            if (r.Count == 0 && m.QuantityOnHand == 0) continue;
            var qty = r.Sum(x => x.Quantity);
            var value = r.Sum(x => x.Quantity * x.UnitCost);
            decimal? change = r.Count >= 2 && r[0].UnitCost != 0 ? Money.Round((r[^1].UnitCost - r[0].UnitCost) / r[0].UnitCost * 100m) : null;
            t.AddRow(change > 5 ? RowStyle.Warning : RowStyle.Normal, "Material", m.Id, m.Code, m.Name, qty, Money.Round(value), r.Count > 0 ? r.Min(x => x.UnitCost) : null, r.Count > 0 ? r.Max(x => x.UnitCost) : null,
                qty > 0 ? Money.Round(value / qty, 4) : null, m.PurchaseCost, m.AverageCost, change);
        }
        return t;
    }

    private async Task<ReportTable> RemnantInventory(ReportFilter f)
    {
        var status = f.Status == null ? RemnantStatus.Available : ParseStatus<RemnantStatus>(f.Status);
        var rows = (await _inventory.ListRemnantsAsync(AllRows, status, f.MaterialId)).Items;
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("material", "Col.Material", width: 2f).Col("thk", "Col.Thickness", K.Number, 0.6f).Col("len", "Col.Length", K.Number, 0.6f)
            .Col("wid", "Col.Width", K.Number, 0.6f).Col("area", "Col.Area", K.Number, 0.7f).Col("wh", "Col.Warehouse").Col("job", "Col.SourceJob", width: 0.8f).Col("date", "Col.Date", K.Date, 0.8f)
            .Col("status", "Col.Status", width: 0.8f).Col("cost", "Col.Cost", K.Money, total: true);
        foreach (var r in rows) t.AddRow((_clock.Now.Date - r.Date).Days > 180 && r.Status == RemnantStatus.Available ? RowStyle.Warning : RowStyle.Normal, null, null, r.Code, r.MaterialName, r.Thickness, r.Length, r.Width, r.Area, r.Warehouse, r.SourceJob, r.Date, r.Status, r.Cost);
        t.Notes.Add(L["Rpt.RemnantInventory.Note"]);
        return t;
    }

    private async Task<ReportTable> InventoryValuation(ReportFilter f)
    {
        var rows = (await _materials.ListAsync(AllRows)).Items;
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("name", "Col.Material", width: 2f).Col("kind", "Col.Kind", width: 0.9f).Col("category", "Col.Category", width: 1.1f)
            .Col("unit", "Col.Unit", width: 0.5f).Col("qty", "Col.OnHand", K.Number).Col("avg", "Col.AverageCost", K.Money).Col("value", "Col.StockValue", K.Money, total: true);
        foreach (var kind in Enum.GetValues<MaterialKind>())
        {
            var group = rows.Where(r => r.Kind == kind && (r.QuantityOnHand != 0 || r.StockValue != 0)).ToList();
            if (group.Count == 0) continue;
            foreach (var m in group) t.AddRow(m.QuantityOnHand < 0 ? RowStyle.Negative : RowStyle.Normal, "Material", m.Id, m.Code, m.Name, m.Kind, m.Category, m.Unit, m.QuantityOnHand, m.AverageCost, m.StockValue);
            t.AddRow(RowStyle.Subtotal, null, null, "", L.Enum(kind), "", "", "", null, null, group.Sum(g => g.StockValue));
        }
        var remnants = (await _inventory.ListRemnantsAsync(AllRows, RemnantStatus.Available)).Items;
        if (remnants.Count > 0)
            t.AddRow(RowStyle.Normal, null, null, "", L["Nav.Remnants"], L["Col.Remnant"], "", "PCS", (decimal)remnants.Count, null, remnants.Sum(r => r.Cost));
        return t;
    }

    private async Task<ReportTable> StockMovement(ReportFilter f)
    {
        var rows = (await _inventory.ListTransactionsAsync(AllRows, f.Range, f.MaterialId, ParseStatus<InventoryTxType>(f.Status), f.JobId)).Items.OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();
        var t = new ReportTable().Col("no", "Col.Number", width: 0.8f).Col("date", "Col.Date", K.Date, 0.75f).Col("type", "Col.Type", width: 1.1f).Col("material", "Col.Material", width: 1.8f)
            .Col("wh", "Col.Warehouse", width: 1f).Col("qty", "Col.Quantity", K.Number, 0.6f).Col("cost", "Col.UnitCost", K.Money, 0.7f).Col("value", "Col.Value", K.Money, total: true)
            .Col("after", "Col.BalanceAfter", K.Number, 0.7f).Col("job", "Col.Job", width: 0.8f).Col("ref", "Col.Reference", width: 0.9f).Col("je", "Col.Entry", width: 0.8f);
        foreach (var r in rows) t.AddRow(RowStyle.Normal, r.JobNumber != null ? "Job" : null, null, r.Number, r.Date, r.Type, r.RemnantCode != null ? $"{r.MaterialName} [{r.RemnantCode}]" : r.MaterialName, r.Warehouse, r.Quantity, r.UnitCost, r.TotalCost, r.RemnantCode == null ? r.QuantityAfter : null, r.JobNumber, r.Reference, r.JournalNumber);
        return t;
    }

    private async Task<ReportTable> LowStock(ReportFilter f)
    {
        var rows = (await _materials.ListAsync(AllRows, lowOnly: true)).Items;
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("name", "Col.Material", width: 2.2f).Col("unit", "Col.Unit", width: 0.5f).Col("qty", "Col.OnHand", K.Number).Col("min", "Col.MinimumStock", K.Number)
            .Col("reorder", "Col.ReorderLevel", K.Number).Col("suggested", "Col.SuggestedOrder", K.Number).Col("avg", "Col.AverageCost", K.Money).Col("value", "Col.OrderValue", K.Money, total: true);
        foreach (var m in rows)
        {
            var suggested = Math.Max(0, m.ReorderLevel * 2 - m.QuantityOnHand);
            t.AddRow(m.QuantityOnHand <= m.MinimumStock ? RowStyle.Negative : RowStyle.Warning, "Material", m.Id, m.Code, m.Name, m.Unit, m.QuantityOnHand, m.MinimumStock, m.ReorderLevel, suggested, m.AverageCost, Money.Round(suggested * m.AverageCost));
        }
        return t;
    }

    // ============================================================== trade
    private async Task<ReportTable> PurchaseRegister(ReportFilter f)
    {
        var receipts = (await _purchases.ListReceiptsAsync(AllRows, f.Range, supplierId: f.SupplierId)).Items;
        var invoices = (await _purchases.ListSupplierInvoicesAsync(AllRows, f.Range, supplierId: f.SupplierId)).Items;
        var t = new ReportTable().Col("type", "Col.Type", width: 0.9f).Col("no", "Col.Number", width: 0.9f).Col("date", "Col.Date", K.Date, 0.8f).Col("supplier", "Col.Supplier", width: 1.8f)
            .Col("ref", "Col.Reference", width: 0.9f).Col("subtotal", "Col.Subtotal", K.Money).Col("tax", "Col.Tax", K.Money).Col("total", "Col.Total", K.Money).Col("paid", "Col.Paid", K.Money).Col("status", "Col.Status", width: 0.9f);
        foreach (var r in receipts) t.AddRow(RowStyle.Normal, null, null, L["Doc.Receipt"], r.Number, r.Date, r.Supplier, r.PurchaseOrder, r.Subtotal, null, null, null, r.IsInvoiced ? L["Common.Invoiced"] : L["Common.NotInvoiced"]);
        foreach (var i in invoices) t.AddRow(i.Balance > 0 ? RowStyle.Warning : RowStyle.Normal, null, null, L["Doc.SupplierInvoice"], i.Number, i.Date, i.Supplier, i.SupplierInvoiceNo, i.Subtotal, i.TaxAmount, i.Total, i.PaidAmount, i.Balance > 0 ? L["Common.Open"] : L["Common.Paid"]);
        t.Notes.Add($"{L["Doc.SupplierInvoice"]}: {L.Money(invoices.Sum(i => i.Total))}   {L["Col.Balance"]}: {L.Money(invoices.Sum(i => i.Balance))}");
        t.ShowTotals = false;
        return t;
    }

    private async Task<ReportTable> SalesRegister(ReportFilter f)
    {
        var rows = (await _sales.ListInvoicesAsync(AllRows, DocumentStatus.Posted, f.CustomerId, f.Range)).Items;
        var t = new ReportTable().Col("no", "Col.Invoice", width: 0.9f).Col("date", "Col.Date", K.Date, 0.8f).Col("customer", "Col.Customer", width: 1.7f).Col("job", "Col.Job", width: 0.8f)
            .Col("subtotal", "Col.Subtotal", K.Money, total: true).Col("disc", "Col.Discount", K.Money, total: true).Col("tax", "Col.Tax", K.Money, total: true).Col("total", "Col.Total", K.Money, total: true)
            .Col("paid", "Col.Paid", K.Money, total: true).Col("returned", "Col.Returned", K.Money, total: true).Col("balance", "Col.Balance", K.Money, total: true);
        var today = _clock.Now.Date;
        foreach (var i in rows.OrderBy(i => i.Date).ThenBy(i => i.Number))
            t.AddRow(i.IsOverdue(today) ? RowStyle.Warning : RowStyle.Normal, "Invoice", i.Id, i.Number, i.Date, i.Customer, i.JobNumber, i.Subtotal, i.DiscountAmount, i.TaxAmount, i.Total, i.PaidAmount, i.ReturnedAmount, i.Balance);
        return t;
    }

    private async Task<ReportTable> SalesReturns(ReportFilter f)
    {
        var rows = (await _sales.ListReturnsAsync(AllRows, f.Range, f.CustomerId)).Items;
        var t = new ReportTable().Col("no", "Col.Number", width: 0.9f).Col("date", "Col.Date", K.Date, 0.8f).Col("customer", "Col.Customer", width: 1.6f).Col("invoice", "Col.Invoice", width: 0.9f)
            .Col("subtotal", "Col.NetAmount", K.Money, total: true).Col("tax", "Col.Tax", K.Money, total: true).Col("total", "Col.Total", K.Money, total: true).Col("cogs", "Col.CogsReversed", K.Money, total: true)
            .Col("method", "Col.Refund", width: 0.8f).Col("reason", "Col.Reason", width: 1.8f);
        foreach (var r in rows) t.Add(r.Number, r.Date, r.Customer, r.InvoiceNumber, r.Subtotal, r.TaxAmount, r.Total, r.CogsReversed, r.RefundMethod, r.Reason);
        return t;
    }

    private async Task<ReportTable> ExpenseAnalysis(ReportFilter f)
    {
        var rows = (await _expenses.ListAsync(AllRows, f.Range, jobId: f.JobId, machineId: f.MachineId, status: DocumentStatus.Posted)).Items;
        var t = new ReportTable().Col("category", "Col.Category", width: 1.6f).Col("count", "Col.Records", K.Integer, 0.6f).Col("amount", "Col.Amount", K.Money, total: true).Col("tax", "Col.Tax", K.Money, total: true)
            .Col("total", "Col.Total", K.Money, total: true).Col("jobs", "Col.ChargedToJobs", K.Money, total: true).Col("share", "Col.Share", K.Percent, 0.6f);
        var total = rows.Sum(r => r.Amount);
        foreach (var g in rows.GroupBy(r => r.Category).OrderByDescending(g => g.Sum(x => x.Amount)))
            t.Add(g.Key, g.Count(), g.Sum(x => x.Amount), g.Sum(x => x.TaxAmount), g.Sum(x => x.Total), g.Where(x => x.JobNumber != null).Sum(x => x.Amount), Money.Percent(g.Sum(x => x.Amount), total));
        foreach (var g in rows.Where(r => r.CostCenter != null).GroupBy(r => r.CostCenter!).OrderBy(g => g.Key))
            t.AddRow(RowStyle.Subtotal, null, null, $"{L["Col.CostCenter"]}: {g.Key}", g.Count(), g.Sum(x => x.Amount), null, null, null, Money.Percent(g.Sum(x => x.Amount), total));
        return t;
    }

    private async Task<ReportTable> AccountsReceivable(ReportFilter f)
    {
        await using var db = _f.Create();
        var docs = await OpenInvoicesAsOf(db, f.AsOf, f.CustomerId);
        var names = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
        var t = new ReportTable().Col("no", "Col.Invoice", width: 0.9f).Col("customer", "Col.Customer", width: 1.8f).Col("date", "Col.Date", K.Date, 0.8f).Col("due", "Col.DueDate", K.Date, 0.8f)
            .Col("days", "Col.DaysOverdue", K.Integer, 0.6f).Col("total", "Col.Total", K.Money, total: true).Col("open", "Col.Balance", K.Money, total: true).Col("bucket", "Col.AgingBucket", width: 0.8f);
        foreach (var d in docs.OrderByDescending(d => d.Days))
            t.AddRow(d.Days > 60 ? RowStyle.Negative : d.Days > 0 ? RowStyle.Warning : RowStyle.Normal, "Invoice", d.Id, d.Number, names.GetValueOrDefault(d.CustomerId), d.Date, d.DueDate, Math.Max(0, d.Days), d.Total, d.Open,
                d.Days <= 0 ? L["Aging.Current"] : d.Days <= 30 ? "1-30" : d.Days <= 60 ? "31-60" : d.Days <= 90 ? "61-90" : "90+");
        var ar = await _accounting.SystemBalanceAsync(SystemAccounts.AR, f.AsOf);
        t.Notes.Add($"{L["Rpt.AccountsReceivable.GlBalance"]}: {L.Money(ar)}");
        return t;
    }

    private async Task<ReportTable> AccountsPayable(ReportFilter f)
    {
        await using var db = _f.Create();
        var to = f.AsOf.Date.AddDays(1);
        var apId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AP)).Id;
        var balances = await db.JournalLines.AsNoTracking().Where(l => l.AccountId == apId && l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date < to && l.SupplierId != null && (f.SupplierId == null || l.SupplierId == f.SupplierId))
            .GroupBy(l => l.SupplierId).Select(g => new { g.Key, Balance = g.Sum(x => x.Credit - x.Debit) }).ToListAsync();
        var invoices = await db.SupplierInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Posted && i.Date < to && (f.SupplierId == null || i.SupplierId == f.SupplierId)).ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id);
        var t = new ReportTable().Col("code", "Col.Code", width: 0.8f).Col("supplier", "Col.Supplier", width: 2f).Col("invoices", "Col.OpenInvoices", K.Integer, 0.7f).Col("overdue", "Col.Overdue", K.Money, total: true)
            .Col("notdue", "Col.NotYetDue", K.Money, total: true).Col("balance", "Col.Balance", K.Money, total: true);
        foreach (var b in balances.Where(b => b.Balance != 0).OrderByDescending(b => b.Balance))
        {
            var s = suppliers[b.Key!.Value];
            var open = invoices.Where(i => i.SupplierId == s.Id && i.Balance > 0).ToList();
            var overdue = open.Where(i => i.DueDate < f.AsOf.Date).Sum(i => i.Balance);
            t.AddRow(overdue > 0 ? RowStyle.Warning : RowStyle.Normal, null, null, s.Code, s.Name, open.Count, overdue, b.Balance - overdue, b.Balance);
        }
        return t;
    }

    // ============================================================== accounting
    private async Task<ReportTable> TrialBalance(ReportFilter f)
    {
        var rows = await _accounting.TrialBalanceAsync(f.Range);
        var names = (await _accounting.PostableAccountsAsync()).ToDictionary(a => a.Code, a => L.IsRightToLeft ? a.NameAr : a.NameEn);
        var t = new ReportTable().Col("code", "Col.Code", width: 0.6f).Col("name", "Col.Account", width: 2.2f).Col("od", "Col.OpeningDebit", K.Money, total: true).Col("oc", "Col.OpeningCredit", K.Money, total: true)
            .Col("pd", "Col.PeriodDebit", K.Money, total: true).Col("pc", "Col.PeriodCredit", K.Money, total: true).Col("cd", "Col.ClosingDebit", K.Money, total: true).Col("cc", "Col.ClosingCredit", K.Money, total: true);
        foreach (var r in rows) t.Add(r.Code, names.GetValueOrDefault(r.Code, r.Name), r.OpeningDebit, r.OpeningCredit, r.PeriodDebit, r.PeriodCredit, r.ClosingDebit, r.ClosingCredit);
        var tot = t.Totals();
        t.Notes.Add((decimal)tot[6]! == (decimal)tot[7]! ? L["Rpt.TrialBalance.Balanced"] : L["Rpt.TrialBalance.Unbalanced"]);
        return t;
    }

    private static ReportTable Statement(IEnumerable<StatementLine> lines)
    {
        var t = new ReportTable { ShowTotals = false }.Col("code", "Col.Code", width: 0.6f).Col("name", "Col.Description", width: 3f).Col("amount", "Col.Amount", K.Money, 1.2f);
        foreach (var l in lines)
            t.AddRow(l.IsTotal ? (l.Level == 0 && l.Amount == 0 ? RowStyle.Header : RowStyle.Subtotal) : l.Amount < 0 ? RowStyle.Normal : RowStyle.Normal, null, null, l.Code, (l.Level > 0 && !l.IsTotal ? "    " : "") + l.Name, l.IsTotal && l.Level == 0 && l.Amount == 0 && l.Code.Length == 4 ? null : l.Amount);
        return t;
    }

    private async Task<ReportTable> IncomeStatement(ReportFilter f) => Statement(await _accounting.IncomeStatementAsync(f.Range, L.IsRightToLeft));

    private async Task<ReportTable> BalanceSheet(ReportFilter f)
    {
        var t = Statement(await _accounting.BalanceSheetAsync(f.AsOf, L.IsRightToLeft));
        return t;
    }

    private async Task<ReportTable> CashFlow(ReportFilter f) => Statement(await _accounting.CashFlowAsync(f.Range, L.IsRightToLeft));

    private async Task<ReportTable> GeneralLedger(ReportFilter f)
    {
        var acc = (await _accounting.PostableAccountsAsync()).FirstOrDefault(a => a.Id == f.AccountId) ?? throw new DomainException("Err.NotFound");
        var rows = await _accounting.GeneralLedgerAsync(acc.Id, f.Range, f.CustomerId, null, f.JobId);
        var t = new ReportTable().Col("date", "Col.Date", K.Date, 0.8f).Col("entry", "Col.Entry", width: 0.8f).Col("source", "Col.Source", width: 0.9f).Col("doc", "Col.Document", width: 0.9f)
            .Col("desc", "Col.Description", width: 2.6f).Col("debit", "Col.Debit", K.Money, total: true).Col("credit", "Col.Credit", K.Money, total: true).Col("balance", "Col.Balance", K.Money);
        t.Parameters.Add($"{L["Filter.Account"]}: {acc.Code} {(L.IsRightToLeft ? acc.NameAr : acc.NameEn)}");
        foreach (var r in rows) t.AddRow(r.EntryId == 0 ? RowStyle.Subtotal : RowStyle.Normal, "Journal", r.EntryId == 0 ? null : r.EntryId, r.Date, r.EntryNumber, L.Source(r.SourceType), r.SourceNumber, r.EntryId == 0 ? L["Report.OpeningBalance"] : r.Description, r.Debit, r.Credit, r.Balance);
        return t;
    }
}
