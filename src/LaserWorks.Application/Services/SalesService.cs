using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record InvoiceRow(long Id, string Number, DateTime Date, DateTime DueDate, long CustomerId, string Customer, string? JobNumber, DocumentStatus Status,
    decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal Total, decimal PaidAmount, decimal ReturnedAmount, decimal CogsAmount)
{
    public decimal Balance => Total - PaidAmount - ReturnedAmount;
    public decimal NetRevenue => Subtotal - DiscountAmount;
    public bool IsOverdue(DateTime today) => Status == DocumentStatus.Posted && Balance > 0 && DueDate < today;
}

public sealed record PaymentRow(long Id, string Number, DateTime Date, string Customer, string? InvoiceNumber, decimal Amount, PaymentMethod Method, string? Reference);

public sealed record ReturnRow(long Id, string Number, DateTime Date, string Customer, string InvoiceNumber, decimal Subtotal, decimal TaxAmount, decimal Total, decimal CogsReversed, PaymentMethod RefundMethod, string? Reason);

public sealed record ReturnLineInput(long InvoiceLineId, decimal Quantity, bool Restock, long? WarehouseId);

public sealed class SalesService : ServiceBase
{
    public SalesService(ServiceContext ctx) : base(ctx) { }

    // ------------------------------------------------------------------ invoices
    public async Task<PagedResult<InvoiceRow>> ListInvoicesAsync(PageRequest req, DocumentStatus? status = null, long? customerId = null, DateRange? range = null, bool openOnly = false)
    {
        Demand(AppModule.Sales, Permission.View);
        await using var db = Factory.Create();
        var q = db.SalesInvoices.AsNoTracking().AsQueryable();
        if (status.HasValue) q = q.Where(i => i.Status == status);
        if (customerId.HasValue) q = q.Where(i => i.CustomerId == customerId);
        if (range != null) q = q.Where(i => i.Date >= range.From.Date && i.Date < range.ToExclusive);
        if (openOnly) q = q.Where(i => i.Status == DocumentStatus.Posted && i.Total - i.PaidAmount - i.ReturnedAmount > 0);
        if (req.Search.Norm() is { } s) q = q.Where(i => i.Number.Contains(s) || i.Customer!.Name.Contains(s) || (i.Job != null && i.Job.Number.Contains(s)));
        return await q.SortBy(req.SortBy, req.Descending, e => e.Id, defaultDesc: true).Select(i => new InvoiceRow(i.Id, i.Number, i.Date, i.DueDate, i.CustomerId, i.Customer!.Name, i.Job != null ? i.Job.Number : null, i.Status, i.Subtotal,
                i.DiscountAmount, i.TaxAmount, i.Total, i.PaidAmount, i.ReturnedAmount, i.CogsAmount))
            .ToPagedAsync(req);
    }

    public async Task<SalesInvoice?> GetInvoiceAsync(long id) => await ReadAsync(db => db.SalesInvoices.AsNoTracking().Include(i => i.Customer).Include(i => i.Job)
        .Include(i => i.Lines).ThenInclude(l => l.Material).Include(i => i.Lines).ThenInclude(l => l.Job).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id));

    public async Task<List<Lookup>> OpenInvoicesAsync(long customerId) => await ReadAsync(db => db.SalesInvoices.AsNoTracking()
        .Where(i => i.CustomerId == customerId && i.Status == DocumentStatus.Posted && i.Total - i.PaidAmount - i.ReturnedAmount > 0)
        .OrderBy(i => i.Date).Select(i => new Lookup(i.Id, i.Number, (i.Total - i.PaidAmount - i.ReturnedAmount).ToString("N2"))).ToListAsync());

    public static void CalculateTotals(SalesInvoice inv, int decimals = 2)
    {
        foreach (var l in inv.Lines)
        {
            if (l.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
            if (l.UnitPrice < 0 || l.DiscountAmount < 0) throw new DomainException("Err.NegativeValue");
            if (l.TaxRate is < 0 or > 100) throw new DomainException("Err.PercentRange", "Tax");
            var gross = Money.Round(l.Quantity * l.UnitPrice, decimals);
            if (l.DiscountAmount > gross) throw new DomainException("Err.DiscountExceedsPrice");
            l.NetAmount = gross - l.DiscountAmount;
            l.TaxAmount = Money.Round(l.NetAmount * l.TaxRate / 100m, decimals);
            l.LineTotal = l.NetAmount + l.TaxAmount;
        }
        inv.Subtotal = inv.Lines.Sum(l => Money.Round(l.Quantity * l.UnitPrice, decimals));
        inv.DiscountAmount = inv.Lines.Sum(l => l.DiscountAmount);
        inv.TaxAmount = inv.Lines.Sum(l => l.TaxAmount);
        inv.Total = inv.Lines.Sum(l => l.LineTotal);
    }

    /// <summary>Draft invoice for a completed/delivered job, priced from the job's agreed selling price.</summary>
    public async Task<long> CreateInvoiceFromJobAsync(long jobId)
    {
        Demand(AppModule.Sales, Permission.Create);
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            var job = await db.Jobs.Include(j => j.Customer).Include(j => j.Quotation).FirstOrDefaultAsync(j => j.Id == jobId) ?? throw new DomainException("Err.NotFound");
            if (job.Status is not (JobStatus.Completed or JobStatus.Delivered)) throw new DomainException("Err.JobNotReadyForInvoice");
            if (await db.SalesInvoiceLines.AnyAsync(l => l.JobId == jobId && l.Invoice!.Status != DocumentStatus.Cancelled))
                throw new DomainException("Err.JobAlreadyInvoiced");
            var taxRate = job.Quotation?.TaxRate ?? s.DefaultTaxRate;
            var inv = new SalesInvoice
            {
                Number = await Numbering.NextAsync(db, SequenceKey.Invoice), Date = Now.Date, DueDate = Now.Date.AddDays(job.Customer!.PaymentTermsDays), CustomerId = job.CustomerId,
                JobId = job.Id, QuotationId = job.QuotationId, Status = DocumentStatus.Draft
            };
            inv.Lines.Add(new SalesInvoiceLine
            {
                LineType = InvoiceLineType.Job, JobId = job.Id, Description = $"{job.Number} — {job.Title}", Quantity = job.Quantity,
                UnitPrice = Money.Round(job.SellingPrice / job.Quantity, 6), TaxRate = taxRate
            });
            CalculateTotals(inv, s.DecimalPlaces);
            // Keep the exact agreed price when per-unit rounding would drift.
            var line = inv.Lines[0];
            var diff = job.SellingPrice - line.NetAmount;
            if (diff != 0 && Math.Abs(diff) < 1) { line.Quantity = 1; line.UnitPrice = job.SellingPrice; line.Description += $" ({job.Quantity:0.##} pcs)"; CalculateTotals(inv, s.DecimalPlaces); }
            db.SalesInvoices.Add(inv);
            await db.SaveChangesAsync();
            return inv.Id;
        });
    }

    public async Task<long> SaveInvoiceAsync(SalesInvoice input)
    {
        if (input.CustomerId == 0) throw new DomainException("Err.Required", "Customer");
        if (input.Lines.Count == 0) throw new DomainException("Err.LinesRequired");
        if (input.DueDate.Date < input.Date.Date) throw new DomainException("Err.DueBeforeStart");
        foreach (var l in input.Lines)
        {
            if (l.LineType == InvoiceLineType.StockItem && l.MaterialId == null) throw new DomainException("Err.Required", "Material");
            if (l.LineType == InvoiceLineType.Job && l.JobId == null) throw new DomainException("Err.Required", "Job");
            Validation.Required(l.Description, "Description");
        }
        Demand(AppModule.Sales, input.Id == 0 ? Permission.Create : Permission.Edit);
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            SalesInvoice inv;
            if (input.Id == 0)
            {
                inv = new SalesInvoice { Number = await Numbering.NextAsync(db, SequenceKey.Invoice), Status = DocumentStatus.Draft };
                db.SalesInvoices.Add(inv);
            }
            else
            {
                inv = await db.SalesInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (inv.Status != DocumentStatus.Draft) throw new DomainException("Err.DocumentPosted");
                db.SalesInvoiceLines.RemoveRange(inv.Lines);
                inv.Lines = new();
            }
            inv.Date = input.Date.Date; inv.DueDate = input.DueDate.Date; inv.CustomerId = input.CustomerId; inv.Notes = input.Notes.Norm();
            inv.JobId = input.Lines.FirstOrDefault(l => l.JobId != null)?.JobId ?? input.JobId; inv.QuotationId = input.QuotationId;
            foreach (var l in input.Lines)
                inv.Lines.Add(new SalesInvoiceLine
                {
                    LineType = l.LineType, JobId = l.JobId, MaterialId = l.MaterialId, WarehouseId = l.WarehouseId, Description = l.Description.Trim(), Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, TaxRate = l.TaxRate
                });
            CalculateTotals(inv, s.DecimalPlaces);
            await db.SaveChangesAsync();
            return inv.Id;
        });
    }

    public async Task DeleteInvoiceAsync(long id)
    {
        Demand(AppModule.Sales, Permission.Delete);
        await using var db = Factory.Create();
        var inv = await db.SalesInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id) ?? throw new DomainException("Err.NotFound");
        if (inv.Status != DocumentStatus.Draft) throw new DomainException("Err.DocumentPosted");
        db.SalesInvoices.Remove(inv);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Posts an invoice:
    ///   Revenue:    Dr Accounts Receivable (total) / Cr Sales (net) / Cr Output Tax (tax)
    ///   Stock item: issue at moving average; Dr COGS / Cr Inventory; unit cost captured on the line for later returns
    ///   Job line:   remaining job WIP moves to COGS (Dr COGS / Cr WIP); job becomes Invoiced
    /// </summary>
    public async Task PostInvoiceAsync(long id, bool ignoreCreditLimit = false)
    {
        Demand(AppModule.Sales, Permission.Post);
        var s = await SettingsAsync();
        await TxAsync(async db =>
        {
            var inv = await db.SalesInvoices.Include(i => i.Lines).Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == id) ?? throw new DomainException("Err.NotFound");
            if (inv.Status != DocumentStatus.Draft) throw new DomainException("Err.DocumentPosted");
            if (inv.Lines.Count == 0) throw new DomainException("Err.LinesRequired");
            CalculateTotals(inv, s.DecimalPlaces);
            var customer = inv.Customer!;
            if (!ignoreCreditLimit && customer.CreditLimit > 0)
            {
                var ar = await AccountingEngine.AccountAsync(db, SystemAccounts.AR);
                var balance = await db.JournalLines.Where(l => l.AccountId == ar.Id && l.CustomerId == customer.Id).SumAsync(l => l.Debit - l.Credit);
                if (balance + inv.Total > customer.CreditLimit) throw new DomainException("Err.CreditLimitExceeded", customer.CreditLimit, balance + inv.Total);
            }
            var custTags = new LineTags(CustomerId: customer.Id);
            var draft = new JournalDraft { Date = inv.Date, Description = $"Sales invoice {inv.Number} — {customer.Name}", SourceType = "SalesInvoice", SourceId = inv.Id, SourceNumber = inv.Number }
                .Dr(SystemAccounts.AR, inv.Total, tags: custTags)
                .Cr(SystemAccounts.Sales, inv.Subtotal - inv.DiscountAmount, tags: custTags)
                .Cr(SystemAccounts.OutputTax, inv.TaxAmount);
            var warehouse = s.DefaultWarehouseId ?? await db.Warehouses.OrderBy(w => w.Id).Select(w => w.Id).FirstAsync();
            decimal cogs = 0;
            var stockTx = new List<InventoryTransaction>();
            foreach (var l in inv.Lines)
            {
                switch (l.LineType)
                {
                    case InvoiceLineType.StockItem:
                    {
                        var m = await db.Materials.FirstAsync(x => x.Id == l.MaterialId);
                        var tx = await InventoryEngine.IssueAsync(db, m, new InventoryEngine.Movement(InventoryTxType.SalesIssue, inv.Date, l.WarehouseId ?? warehouse, l.Quantity,
                            SourceType: "SalesInvoice", SourceId: inv.Id, Reference: inv.Number));
                        l.CogsAmount = -tx.TotalCost;
                        l.UnitCost = tx.UnitCost;
                        l.WarehouseId ??= warehouse;
                        draft.Dr(SystemAccounts.COGS, l.CogsAmount, $"{m.Code} × {l.Quantity:0.###}").Cr(AccountingEngine.InventoryAccountKey(m.Kind), l.CogsAmount);
                        stockTx.Add(tx);
                        break;
                    }
                    case InvoiceLineType.Job:
                    {
                        var job = await db.Jobs.FirstAsync(j => j.Id == l.JobId);
                        if (job.CustomerId != inv.CustomerId) throw new DomainException("Err.JobCustomerMismatch");
                        if (job.Status is not (JobStatus.Completed or JobStatus.Delivered)) throw new DomainException("Err.JobNotReadyForInvoice");
                        if (await db.SalesInvoiceLines.AnyAsync(x => x.JobId == job.Id && x.InvoiceId != inv.Id && x.Invoice!.Status == DocumentStatus.Posted))
                            throw new DomainException("Err.JobAlreadyInvoiced");
                        await ProductionService.ApplyOverheadAsync(db, job, Now, UserName);
                        var amount = job.ActualCost - job.CostTransferredToCogs;
                        l.CogsAmount = amount;
                        l.UnitCost = l.Quantity == 0 ? 0 : Math.Round(amount / l.Quantity, 6);
                        draft.Dr(SystemAccounts.COGS, amount, $"Job {job.Number}", new LineTags(JobId: job.Id)).Cr(SystemAccounts.WIP, amount, tags: new LineTags(JobId: job.Id));
                        job.CostTransferredToCogs += amount;
                        job.InvoicedRevenue += l.NetAmount;
                        job.Status = JobStatus.Invoiced;
                        job.InvoicedAt = Now;
                        break;
                    }
                }
                cogs += l.CogsAmount;
            }
            inv.CogsAmount = cogs;
            inv.Status = DocumentStatus.Posted;
            inv.PostedAt = Now;
            var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
            inv.JournalEntryId = null;
            foreach (var tx in stockTx) tx.JournalEntry = je;
            await db.SaveChangesAsync();
            inv.JournalEntryId = je!.Id;
            Audit(db, AuditAction.Posted, nameof(SalesInvoice), inv.Id, inv.Number);
        });
    }

    // ------------------------------------------------------------------ payments
    public async Task<PagedResult<PaymentRow>> ListPaymentsAsync(PageRequest req, long? customerId = null, DateRange? range = null)
    {
        Demand(AppModule.Sales, Permission.View);
        await using var db = Factory.Create();
        var q = db.CustomerPayments.AsNoTracking().AsQueryable();
        if (customerId.HasValue) q = q.Where(p => p.CustomerId == customerId);
        if (range != null) q = q.Where(p => p.Date >= range.From.Date && p.Date < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(p => p.Number.Contains(s) || p.Customer!.Name.Contains(s) || (p.Reference != null && p.Reference.Contains(s)));
        return await q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new PaymentRow(p.Id, p.Number, p.Date, p.Customer!.Name, p.Invoice != null ? p.Invoice.Number : null, p.Amount, p.Method, p.Reference)).ToPagedAsync(req);
    }

    /// <summary>Customer receipt: Dr Cash/Bank / Cr Accounts Receivable. Optionally allocated to one invoice.</summary>
    public async Task<long> RecordPaymentAsync(long customerId, long? invoiceId, decimal amount, PaymentMethod method, DateTime date, string? reference, string? notes = null)
    {
        Demand(AppModule.Sales, Permission.Post);
        if (amount <= 0) throw new DomainException("Err.AmountPositive");
        if (method == PaymentMethod.OnCredit) throw new DomainException("Err.PaymentMethodNotCash");
        return await TxAsync(async db =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId) ?? throw new DomainException("Err.NotFound");
            SalesInvoice? inv = null;
            if (invoiceId is { } iid)
            {
                inv = await db.SalesInvoices.FirstOrDefaultAsync(i => i.Id == iid) ?? throw new DomainException("Err.NotFound");
                if (inv.CustomerId != customerId) throw new DomainException("Err.InvoiceCustomerMismatch");
                if (inv.Status != DocumentStatus.Posted) throw new DomainException("Err.InvoiceNotPosted");
                if (amount > inv.Balance) throw new DomainException("Err.PaymentExceedsBalance", inv.Balance, amount);
                inv.PaidAmount += amount;
            }
            var p = new CustomerPayment { Number = await Numbering.NextAsync(db, SequenceKey.CustomerPayment), Date = date.Date, CustomerId = customerId, InvoiceId = invoiceId, Amount = Money.Round(amount), Method = method, Reference = reference.Norm(), Notes = notes.Norm() };
            db.CustomerPayments.Add(p);
            await db.SaveChangesAsync();
            var je = await AccountingEngine.PostAsync(db, new JournalDraft { Date = p.Date, Description = $"Receipt {p.Number} — {customer.Name}" + (inv != null ? $" for {inv.Number}" : ""), SourceType = "CustomerPayment", SourceId = p.Id, SourceNumber = p.Number }
                .Dr(AccountingEngine.CashAccountKey(method), p.Amount).Cr(SystemAccounts.AR, p.Amount, tags: new LineTags(CustomerId: customerId)), UserName, Now);
            await db.SaveChangesAsync();
            p.JournalEntryId = je!.Id;
            Audit(db, AuditAction.Posted, nameof(CustomerPayment), p.Id, p.Number);
            return p.Id;
        });
    }

    // ------------------------------------------------------------------ returns
    public async Task<PagedResult<ReturnRow>> ListReturnsAsync(PageRequest req, DateRange? range = null, long? customerId = null)
    {
        Demand(AppModule.Sales, Permission.View);
        await using var db = Factory.Create();
        var q = db.SalesReturns.AsNoTracking().Where(r => r.Status == DocumentStatus.Posted);
        if (range != null) q = q.Where(r => r.Date >= range.From.Date && r.Date < range.ToExclusive);
        if (customerId.HasValue) q = q.Where(r => r.CustomerId == customerId);
        if (req.Search.Norm() is { } s) q = q.Where(r => r.Number.Contains(s) || r.Customer!.Name.Contains(s) || r.Invoice!.Number.Contains(s));
        return await q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .Select(r => new ReturnRow(r.Id, r.Number, r.Date, r.Customer!.Name, r.Invoice!.Number, r.Subtotal, r.TaxAmount, r.Total, r.CogsReversed, r.RefundMethod, r.Reason)).ToPagedAsync(req);
    }

    /// <summary>
    /// Sales return against a posted invoice.
    ///   Revenue reversal: Dr Sales Returns (net) + Dr Output Tax / Cr Accounts Receivable (or Cr Cash/Bank for a refund)
    ///   Restocked stock items: received back at the ORIGINAL unit cost from the invoice line — Dr Inventory / Cr COGS.
    ///   Job lines reverse revenue only (custom work is not returned to stock).
    /// </summary>
    public async Task<long> CreateReturnAsync(long invoiceId, IReadOnlyList<ReturnLineInput> lines, DateTime date, string? reason, PaymentMethod refundMethod = PaymentMethod.OnCredit)
    {
        Demand(AppModule.Sales, Permission.Post);
        if (lines.Count == 0 || lines.All(l => l.Quantity == 0)) throw new DomainException("Err.LinesRequired");
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            var inv = await db.SalesInvoices.Include(i => i.Lines).Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == invoiceId) ?? throw new DomainException("Err.NotFound");
            if (inv.Status != DocumentStatus.Posted) throw new DomainException("Err.InvoiceNotPosted");
            if (date.Date < inv.Date.Date) throw new DomainException("Err.ReturnBeforeInvoice");
            var ret = new SalesReturn { Number = await Numbering.NextAsync(db, SequenceKey.SalesReturn), Date = date.Date, InvoiceId = inv.Id, CustomerId = inv.CustomerId, Reason = reason.Norm(), RefundMethod = refundMethod, Status = DocumentStatus.Posted };
            var warehouse = s.DefaultWarehouseId ?? await db.Warehouses.OrderBy(w => w.Id).Select(w => w.Id).FirstAsync();
            foreach (var input in lines.Where(l => l.Quantity != 0))
            {
                if (input.Quantity < 0) throw new DomainException("Err.QuantityPositive");
                var line = inv.Lines.FirstOrDefault(l => l.Id == input.InvoiceLineId) ?? throw new DomainException("Err.NotFound");
                if (input.Quantity > line.Quantity - line.ReturnedQuantity) throw new DomainException("Err.ReturnExceedsSold", line.Quantity - line.ReturnedQuantity, input.Quantity);
                var fraction = input.Quantity / line.Quantity;
                var net = input.Quantity == line.Quantity - line.ReturnedQuantity && line.ReturnedQuantity == 0 ? line.NetAmount : Money.Round(line.NetAmount * fraction, s.DecimalPlaces);
                var tax = input.Quantity == line.Quantity - line.ReturnedQuantity && line.ReturnedQuantity == 0 ? line.TaxAmount : Money.Round(line.TaxAmount * fraction, s.DecimalPlaces);
                var restock = input.Restock && line.LineType == InvoiceLineType.StockItem;
                var rl = new SalesReturnLine
                {
                    InvoiceLineId = line.Id, Quantity = input.Quantity, UnitPrice = line.UnitPrice, NetAmount = net, TaxAmount = tax, UnitCost = line.UnitCost,
                    CogsAmount = restock ? Money.Round(line.UnitCost * input.Quantity, s.DecimalPlaces) : 0, Restock = restock, WarehouseId = restock ? input.WarehouseId ?? line.WarehouseId ?? warehouse : null
                };
                if (restock && rl.CogsAmount > line.CogsAmount) rl.CogsAmount = line.CogsAmount;
                line.ReturnedQuantity += input.Quantity;
                ret.Lines.Add(rl);
            }
            ret.Subtotal = ret.Lines.Sum(l => l.NetAmount);
            ret.TaxAmount = ret.Lines.Sum(l => l.TaxAmount);
            ret.Total = ret.Subtotal + ret.TaxAmount;
            ret.CogsReversed = ret.Lines.Sum(l => l.CogsAmount);
            db.SalesReturns.Add(ret);
            await db.SaveChangesAsync();

            var custTags = new LineTags(CustomerId: inv.CustomerId);
            var draft = new JournalDraft { Date = ret.Date, Description = $"Sales return {ret.Number} for {inv.Number}", SourceType = "SalesReturn", SourceId = ret.Id, SourceNumber = ret.Number }
                .Dr(SystemAccounts.SalesReturns, ret.Subtotal, tags: custTags)
                .Dr(SystemAccounts.OutputTax, ret.TaxAmount);
            if (refundMethod == PaymentMethod.OnCredit)
            {
                draft.Cr(SystemAccounts.AR, ret.Total, tags: custTags);
                inv.ReturnedAmount += ret.Total;
            }
            else
            {
                draft.Cr(AccountingEngine.CashAccountKey(refundMethod), ret.Total);
                inv.ReturnedAmount += ret.Total;
                inv.PaidAmount -= ret.Total; // cash refunded: the invoice balance stays unchanged
            }
            var txs = new List<InventoryTransaction>();
            foreach (var rl in ret.Lines)
            {
                var line = inv.Lines.First(l => l.Id == rl.InvoiceLineId);
                if (rl.Restock)
                {
                    var m = await db.Materials.FirstAsync(x => x.Id == line.MaterialId);
                    var tx = await InventoryEngine.ReceiveAsync(db, m, new InventoryEngine.Movement(InventoryTxType.SalesReturn, ret.Date, rl.WarehouseId!.Value, rl.Quantity, rl.CogsAmount / rl.Quantity,
                        SourceType: "SalesReturn", SourceId: ret.Id, Reference: ret.Number));
                    draft.Dr(AccountingEngine.InventoryAccountKey(m.Kind), tx.TotalCost, $"{m.Code} × {rl.Quantity:0.###} @ original cost").Cr(SystemAccounts.COGS, tx.TotalCost);
                    txs.Add(tx);
                }
                if (line.LineType == InvoiceLineType.Job && line.JobId is { } jid)
                {
                    var job = await db.Jobs.FirstAsync(j => j.Id == jid);
                    job.InvoicedRevenue -= rl.NetAmount;
                }
            }
            var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
            foreach (var tx in txs) tx.JournalEntry = je;
            await db.SaveChangesAsync();
            ret.JournalEntryId = je!.Id;
            Audit(db, AuditAction.Posted, nameof(SalesReturn), ret.Id, ret.Number);
            return ret.Id;
        });
    }
}
