using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record PurchaseOrderRow(long Id, string Number, DateTime Date, DateTime? ExpectedDate, string Supplier, PurchaseOrderStatus Status, decimal Subtotal, decimal TaxAmount, decimal Total);
public sealed record ReceiptRow(long Id, string Number, DateTime Date, string Supplier, string? PurchaseOrder, string Warehouse, DocumentStatus Status, decimal Subtotal, bool IsInvoiced);
public sealed record SupplierInvoiceRow(long Id, string Number, string? SupplierInvoiceNo, DateTime Date, DateTime DueDate, string Supplier, string? Receipt, DocumentStatus Status,
    decimal Subtotal, decimal TaxAmount, decimal Total, decimal PaidAmount, decimal ReturnedAmount)
{
    public decimal Balance => Total - PaidAmount - ReturnedAmount;
}
public sealed record SupplierPaymentRow(long Id, string Number, DateTime Date, string Supplier, string? Invoice, decimal Amount, PaymentMethod Method, string? Reference);
public sealed record PurchaseReturnRow(long Id, string Number, DateTime Date, string Supplier, string Receipt, decimal Subtotal, decimal TaxAmount, decimal Total, string? Reason);
public sealed record PurchaseReturnLineInput(long ReceiptLineId, decimal Quantity);

public sealed class PurchaseService : ServiceBase
{
    public PurchaseService(ServiceContext ctx) : base(ctx) { }

    // ------------------------------------------------------------------ purchase orders
    public async Task<PagedResult<PurchaseOrderRow>> ListOrdersAsync(PageRequest req, PurchaseOrderStatus? status = null, DateRange? range = null)
    {
        Demand(AppModule.Purchases, Permission.View);
        await using var db = Factory.Create();
        var q = db.PurchaseOrders.AsNoTracking().AsQueryable();
        if (status.HasValue) q = q.Where(p => p.Status == status);
        if (range != null) q = q.Where(p => p.Date >= range.From.Date && p.Date < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(p => p.Number.Contains(s) || p.Supplier!.Name.Contains(s));
        return await q.OrderByDescending(p => p.Id).Select(p => new PurchaseOrderRow(p.Id, p.Number, p.Date, p.ExpectedDate, p.Supplier!.Name, p.Status, p.Subtotal, p.TaxAmount, p.Total)).ToPagedAsync(req);
    }

    public async Task<PurchaseOrder?> GetOrderAsync(long id) => await ReadAsync(db => db.PurchaseOrders.AsNoTracking().Include(p => p.Supplier).Include(p => p.Lines).ThenInclude(l => l.Material).FirstOrDefaultAsync(p => p.Id == id));

    public async Task<List<Lookup>> OpenOrdersAsync(long? supplierId = null) => await ReadAsync(db => db.PurchaseOrders.AsNoTracking()
        .Where(p => (p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived) && (supplierId == null || p.SupplierId == supplierId))
        .OrderByDescending(p => p.Id).Select(p => new Lookup(p.Id, p.Number, p.Supplier!.Name)).ToListAsync());

    public async Task<long> SaveOrderAsync(PurchaseOrder input)
    {
        if (input.SupplierId == 0) throw new DomainException("Err.Required", "Supplier");
        if (input.Lines.Count == 0) throw new DomainException("Err.LinesRequired");
        foreach (var l in input.Lines)
        {
            if (l.MaterialId == 0) throw new DomainException("Err.Required", "Material");
            if (l.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
            if (l.UnitCost < 0 || l.TaxRate < 0) throw new DomainException("Err.NegativeValue");
        }
        Demand(AppModule.Purchases, input.Id == 0 ? Permission.Create : Permission.Edit);
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            PurchaseOrder po;
            if (input.Id == 0)
            {
                po = new PurchaseOrder { Number = await Numbering.NextAsync(db, SequenceKey.PurchaseOrder), Status = PurchaseOrderStatus.Draft };
                db.PurchaseOrders.Add(po);
            }
            else
            {
                po = await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (po.Status != PurchaseOrderStatus.Draft) throw new DomainException("Err.DocumentPosted");
                db.PurchaseOrderLines.RemoveRange(po.Lines);
                po.Lines = new();
            }
            po.SupplierId = input.SupplierId; po.Date = input.Date.Date; po.ExpectedDate = input.ExpectedDate?.Date; po.Notes = input.Notes.Norm();
            foreach (var l in input.Lines)
                po.Lines.Add(new PurchaseOrderLine { MaterialId = l.MaterialId, Quantity = l.Quantity, UnitCost = l.UnitCost, TaxRate = l.TaxRate, LineTotal = Money.Round(l.Quantity * l.UnitCost, s.DecimalPlaces) });
            po.Subtotal = po.Lines.Sum(l => l.LineTotal);
            po.TaxAmount = po.Lines.Sum(l => Money.Round(l.LineTotal * l.TaxRate / 100m, s.DecimalPlaces));
            po.Total = po.Subtotal + po.TaxAmount;
            await db.SaveChangesAsync();
            return po.Id;
        });
    }

    public async Task SetOrderStatusAsync(long id, PurchaseOrderStatus status)
    {
        Demand(AppModule.Purchases, status == PurchaseOrderStatus.Approved ? Permission.Approve : Permission.Edit);
        await TxAsync(async db =>
        {
            var po = await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id) ?? throw new DomainException("Err.NotFound");
            var ok = (po.Status, status) switch
            {
                (PurchaseOrderStatus.Draft, PurchaseOrderStatus.Approved) => true,
                (PurchaseOrderStatus.Draft, PurchaseOrderStatus.Cancelled) => true,
                (PurchaseOrderStatus.Approved, PurchaseOrderStatus.Cancelled) => po.Lines.All(l => l.ReceivedQuantity == 0),
                (PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received) => true, // close short
                _ => false
            };
            if (!ok) throw new DomainException("Err.InvalidStatusTransition", po.Status, status);
            po.Status = status;
            Audit(db, status == PurchaseOrderStatus.Approved ? AuditAction.Approved : AuditAction.StatusChanged, nameof(PurchaseOrder), id, status.ToString());
        });
    }

    // ------------------------------------------------------------------ receipts
    public async Task<PagedResult<ReceiptRow>> ListReceiptsAsync(PageRequest req, DateRange? range = null, bool? invoiced = null, long? supplierId = null)
    {
        Demand(AppModule.Purchases, Permission.View);
        await using var db = Factory.Create();
        var q = db.PurchaseReceipts.AsNoTracking().AsQueryable();
        if (range != null) q = q.Where(p => p.Date >= range.From.Date && p.Date < range.ToExclusive);
        if (invoiced.HasValue) q = q.Where(p => p.IsInvoiced == invoiced && p.Status == DocumentStatus.Posted);
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId);
        if (req.Search.Norm() is { } s) q = q.Where(p => p.Number.Contains(s) || p.Supplier!.Name.Contains(s));
        return await q.OrderByDescending(p => p.Id).Select(p => new ReceiptRow(p.Id, p.Number, p.Date, p.Supplier!.Name, p.PurchaseOrder != null ? p.PurchaseOrder.Number : null, p.Warehouse!.Name, p.Status, p.Subtotal, p.IsInvoiced)).ToPagedAsync(req);
    }

    public async Task<PurchaseReceipt?> GetReceiptAsync(long id) => await ReadAsync(db => db.PurchaseReceipts.AsNoTracking().Include(p => p.Supplier).Include(p => p.Warehouse).Include(p => p.Lines).ThenInclude(l => l.Material).FirstOrDefaultAsync(p => p.Id == id));

    /// <summary>Builds an unsaved receipt with the outstanding quantities of an approved purchase order.</summary>
    public async Task<PurchaseReceipt> ReceiptFromOrderAsync(long orderId)
    {
        var po = await GetOrderAsync(orderId) ?? throw new DomainException("Err.NotFound");
        if (po.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived)) throw new DomainException("Err.OrderNotApproved");
        var s = await SettingsAsync();
        var r = new PurchaseReceipt { SupplierId = po.SupplierId, PurchaseOrderId = po.Id, Date = Now.Date, WarehouseId = s.DefaultWarehouseId ?? 0 };
        foreach (var l in po.Lines.Where(l => l.Quantity > l.ReceivedQuantity))
            r.Lines.Add(new PurchaseReceiptLine { PurchaseOrderLineId = l.Id, MaterialId = l.MaterialId, Material = l.Material, Quantity = l.Quantity - l.ReceivedQuantity, UnitCost = l.UnitCost, TaxRate = l.TaxRate });
        return r;
    }

    /// <summary>Posts a goods receipt: stock in at purchase cost (moving average updated). Dr Inventory / Cr Goods Received Not Invoiced.</summary>
    public async Task<long> PostReceiptAsync(PurchaseReceipt input)
    {
        Demand(AppModule.Purchases, Permission.Post);
        if (input.SupplierId == 0) throw new DomainException("Err.Required", "Supplier");
        if (input.WarehouseId == 0) throw new DomainException("Err.Required", "Warehouse");
        if (input.Lines.Count == 0 || input.Lines.All(l => l.Quantity == 0)) throw new DomainException("Err.LinesRequired");
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            var r = new PurchaseReceipt
            {
                Number = await Numbering.NextAsync(db, SequenceKey.PurchaseReceipt), Date = input.Date.Date, SupplierId = input.SupplierId, PurchaseOrderId = input.PurchaseOrderId,
                WarehouseId = input.WarehouseId, SupplierDeliveryRef = input.SupplierDeliveryRef.Norm(), Notes = input.Notes.Norm(), Status = DocumentStatus.Posted
            };
            PurchaseOrder? po = input.PurchaseOrderId is { } poid ? await db.PurchaseOrders.Include(p => p.Lines).FirstAsync(p => p.Id == poid) : null;
            if (po != null && po.SupplierId != input.SupplierId) throw new DomainException("Err.SupplierMismatch");
            foreach (var l in input.Lines.Where(l => l.Quantity != 0))
            {
                if (l.Quantity < 0) throw new DomainException("Err.QuantityPositive");
                if (l.UnitCost < 0) throw new DomainException("Err.NegativeValue");
                if (po != null && l.PurchaseOrderLineId is { } plid)
                {
                    var pl = po.Lines.First(x => x.Id == plid);
                    pl.ReceivedQuantity += l.Quantity;
                }
                r.Lines.Add(new PurchaseReceiptLine { PurchaseOrderLineId = l.PurchaseOrderLineId, MaterialId = l.MaterialId, Quantity = l.Quantity, UnitCost = l.UnitCost, TaxRate = l.TaxRate, LineTotal = Money.Round(l.Quantity * l.UnitCost, s.DecimalPlaces) });
            }
            r.Subtotal = r.Lines.Sum(l => l.LineTotal);
            if (po != null) po.Status = po.Lines.All(x => x.ReceivedQuantity >= x.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
            db.PurchaseReceipts.Add(r);
            await db.SaveChangesAsync();
            var supplier = await db.Suppliers.FirstAsync(x => x.Id == r.SupplierId);
            var draft = new JournalDraft { Date = r.Date, Description = $"Goods receipt {r.Number} — {supplier.Name}", SourceType = "PurchaseReceipt", SourceId = r.Id, SourceNumber = r.Number };
            var txs = new List<InventoryTransaction>();
            foreach (var l in r.Lines)
            {
                var m = await db.Materials.FirstAsync(x => x.Id == l.MaterialId);
                var tx = await InventoryEngine.ReceiveAsync(db, m, new InventoryEngine.Movement(InventoryTxType.PurchaseReceipt, r.Date, r.WarehouseId, l.Quantity, l.UnitCost, SourceType: "PurchaseReceipt", SourceId: r.Id, Reference: r.Number));
                m.PurchaseCost = l.UnitCost;
                draft.Dr(AccountingEngine.InventoryAccountKey(m.Kind), tx.TotalCost, $"{m.Code} × {l.Quantity:0.###}");
                txs.Add(tx);
            }
            draft.Cr(SystemAccounts.GRNI, txs.Sum(t => t.TotalCost), tags: new LineTags(SupplierId: supplier.Id));
            var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
            foreach (var tx in txs) tx.JournalEntry = je;
            await db.SaveChangesAsync();
            r.JournalEntryId = je?.Id;
            Audit(db, AuditAction.Posted, nameof(PurchaseReceipt), r.Id, r.Number);
            return r.Id;
        });
    }

    // ------------------------------------------------------------------ supplier invoices
    public async Task<PagedResult<SupplierInvoiceRow>> ListSupplierInvoicesAsync(PageRequest req, DateRange? range = null, bool openOnly = false, long? supplierId = null)
    {
        Demand(AppModule.Purchases, Permission.View);
        await using var db = Factory.Create();
        var q = db.SupplierInvoices.AsNoTracking().AsQueryable();
        if (range != null) q = q.Where(p => p.Date >= range.From.Date && p.Date < range.ToExclusive);
        if (openOnly) q = q.Where(p => p.Status == DocumentStatus.Posted && p.Total - p.PaidAmount - p.ReturnedAmount > 0);
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId);
        if (req.Search.Norm() is { } s) q = q.Where(p => p.Number.Contains(s) || p.Supplier!.Name.Contains(s) || (p.SupplierInvoiceNo != null && p.SupplierInvoiceNo.Contains(s)));
        return await q.OrderByDescending(p => p.Id).Select(p => new SupplierInvoiceRow(p.Id, p.Number, p.SupplierInvoiceNo, p.Date, p.DueDate, p.Supplier!.Name, p.Receipt != null ? p.Receipt.Number : null,
            p.Status, p.Subtotal, p.TaxAmount, p.Total, p.PaidAmount, p.ReturnedAmount)).ToPagedAsync(req);
    }

    public async Task<List<Lookup>> OpenSupplierInvoicesAsync(long supplierId) => await ReadAsync(db => db.SupplierInvoices.AsNoTracking()
        .Where(i => i.SupplierId == supplierId && i.Status == DocumentStatus.Posted && i.Total - i.PaidAmount - i.ReturnedAmount > 0)
        .Select(i => new Lookup(i.Id, i.Number, (i.Total - i.PaidAmount - i.ReturnedAmount).ToString("N2"))).ToListAsync());

    /// <summary>Supplier invoice matched to a receipt: Dr GRNI (receipt value) + Dr Input Tax / Cr Accounts Payable.</summary>
    public async Task<long> PostSupplierInvoiceFromReceiptAsync(long receiptId, string? supplierInvoiceNo, DateTime date, DateTime? dueDate = null)
    {
        Demand(AppModule.Purchases, Permission.Post);
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            var r = await db.PurchaseReceipts.Include(x => x.Lines).Include(x => x.Supplier).FirstOrDefaultAsync(x => x.Id == receiptId) ?? throw new DomainException("Err.NotFound");
            if (r.Status != DocumentStatus.Posted) throw new DomainException("Err.DocumentNotPosted");
            if (r.IsInvoiced) throw new DomainException("Err.ReceiptAlreadyInvoiced");
            var lines = r.Lines.Where(l => l.Quantity > l.ReturnedQuantity).ToList();
            var subtotal = lines.Sum(l => Money.Round((l.Quantity - l.ReturnedQuantity) * l.UnitCost, s.DecimalPlaces));
            var tax = lines.Sum(l => Money.Round(Money.Round((l.Quantity - l.ReturnedQuantity) * l.UnitCost, s.DecimalPlaces) * l.TaxRate / 100m, s.DecimalPlaces));
            if (subtotal <= 0) throw new DomainException("Err.NothingToInvoice");
            var inv = new SupplierInvoice
            {
                Number = await Numbering.NextAsync(db, SequenceKey.SupplierInvoice), SupplierInvoiceNo = supplierInvoiceNo.Norm(), Date = date.Date, DueDate = (dueDate ?? date.AddDays(r.Supplier!.PaymentTermsDays)).Date,
                SupplierId = r.SupplierId, ReceiptId = r.Id, Status = DocumentStatus.Posted, Subtotal = subtotal, TaxAmount = tax, Total = subtotal + tax
            };
            r.IsInvoiced = true;
            db.SupplierInvoices.Add(inv);
            await db.SaveChangesAsync();
            var tags = new LineTags(SupplierId: r.SupplierId);
            var je = await AccountingEngine.PostAsync(db, new JournalDraft { Date = inv.Date, Description = $"Supplier invoice {inv.Number} ({inv.SupplierInvoiceNo}) — {r.Supplier!.Name}", SourceType = "SupplierInvoice", SourceId = inv.Id, SourceNumber = inv.Number }
                .Dr(SystemAccounts.GRNI, subtotal, tags: tags).Dr(SystemAccounts.InputTax, tax).Cr(SystemAccounts.AP, inv.Total, tags: tags), UserName, Now);
            await db.SaveChangesAsync();
            inv.JournalEntryId = je?.Id;
            Audit(db, AuditAction.Posted, nameof(SupplierInvoice), inv.Id, inv.Number);
            return inv.Id;
        });
    }

    // ------------------------------------------------------------------ supplier payments
    public async Task<PagedResult<SupplierPaymentRow>> ListSupplierPaymentsAsync(PageRequest req, DateRange? range = null)
    {
        Demand(AppModule.Purchases, Permission.View);
        await using var db = Factory.Create();
        var q = db.SupplierPayments.AsNoTracking().AsQueryable();
        if (range != null) q = q.Where(p => p.Date >= range.From.Date && p.Date < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(p => p.Number.Contains(s) || p.Supplier!.Name.Contains(s));
        return await q.OrderByDescending(p => p.Id).Select(p => new SupplierPaymentRow(p.Id, p.Number, p.Date, p.Supplier!.Name, p.SupplierInvoice != null ? p.SupplierInvoice.Number : null, p.Amount, p.Method, p.Reference)).ToPagedAsync(req);
    }

    /// <summary>Dr Accounts Payable / Cr Cash or Bank.</summary>
    public async Task<long> PaySupplierAsync(long supplierId, long? invoiceId, decimal amount, PaymentMethod method, DateTime date, string? reference)
    {
        Demand(AppModule.Purchases, Permission.Post);
        if (amount <= 0) throw new DomainException("Err.AmountPositive");
        if (method == PaymentMethod.OnCredit) throw new DomainException("Err.PaymentMethodNotCash");
        return await TxAsync(async db =>
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == supplierId) ?? throw new DomainException("Err.NotFound");
            if (invoiceId is { } iid)
            {
                var inv = await db.SupplierInvoices.FirstAsync(x => x.Id == iid);
                if (inv.SupplierId != supplierId) throw new DomainException("Err.SupplierMismatch");
                if (amount > inv.Balance) throw new DomainException("Err.PaymentExceedsBalance", inv.Balance, amount);
                inv.PaidAmount += amount;
            }
            var p = new SupplierPayment { Number = await Numbering.NextAsync(db, SequenceKey.SupplierPayment), Date = date.Date, SupplierId = supplierId, SupplierInvoiceId = invoiceId, Amount = Money.Round(amount), Method = method, Reference = reference.Norm() };
            db.SupplierPayments.Add(p);
            await db.SaveChangesAsync();
            var je = await AccountingEngine.PostAsync(db, new JournalDraft { Date = p.Date, Description = $"Payment {p.Number} to {supplier.Name}", SourceType = "SupplierPayment", SourceId = p.Id, SourceNumber = p.Number }
                .Dr(SystemAccounts.AP, p.Amount, tags: new LineTags(SupplierId: supplierId)).Cr(AccountingEngine.CashAccountKey(method), p.Amount), UserName, Now);
            await db.SaveChangesAsync();
            p.JournalEntryId = je?.Id;
            Audit(db, AuditAction.Posted, nameof(SupplierPayment), p.Id, p.Number);
            return p.Id;
        });
    }

    // ------------------------------------------------------------------ purchase returns
    public async Task<PagedResult<PurchaseReturnRow>> ListReturnsAsync(PageRequest req, DateRange? range = null)
    {
        Demand(AppModule.Purchases, Permission.View);
        await using var db = Factory.Create();
        var q = db.PurchaseReturns.AsNoTracking().AsQueryable();
        if (range != null) q = q.Where(p => p.Date >= range.From.Date && p.Date < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(p => p.Number.Contains(s) || p.Supplier!.Name.Contains(s));
        return await q.OrderByDescending(p => p.Id).Select(p => new PurchaseReturnRow(p.Id, p.Number, p.Date, p.Supplier!.Name, p.Receipt!.Number, p.Subtotal, p.TaxAmount, p.Total, p.Reason)).ToPagedAsync(req);
    }

    /// <summary>
    /// Return goods to the supplier at the original receipt cost.
    /// Not yet invoiced: Dr GRNI / Cr Inventory. Invoiced: Dr Accounts Payable (incl. tax) / Cr Inventory / Cr Input Tax.
    /// </summary>
    public async Task<long> ReturnToSupplierAsync(long receiptId, IReadOnlyList<PurchaseReturnLineInput> lines, DateTime date, string? reason)
    {
        Demand(AppModule.Purchases, Permission.Post);
        if (lines.Count == 0 || lines.All(l => l.Quantity == 0)) throw new DomainException("Err.LinesRequired");
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            var r = await db.PurchaseReceipts.Include(x => x.Lines).Include(x => x.Supplier).FirstOrDefaultAsync(x => x.Id == receiptId) ?? throw new DomainException("Err.NotFound");
            var ret = new PurchaseReturn { Number = await Numbering.NextAsync(db, SequenceKey.PurchaseReturn), Date = date.Date, SupplierId = r.SupplierId, ReceiptId = r.Id, Reason = reason.Norm(), Status = DocumentStatus.Posted };
            foreach (var li in lines.Where(l => l.Quantity != 0))
            {
                if (li.Quantity < 0) throw new DomainException("Err.QuantityPositive");
                var rl = r.Lines.FirstOrDefault(x => x.Id == li.ReceiptLineId) ?? throw new DomainException("Err.NotFound");
                if (li.Quantity > rl.Quantity - rl.ReturnedQuantity) throw new DomainException("Err.ReturnExceedsReceived", rl.Quantity - rl.ReturnedQuantity, li.Quantity);
                rl.ReturnedQuantity += li.Quantity;
                ret.Lines.Add(new PurchaseReturnLine { ReceiptLineId = rl.Id, MaterialId = rl.MaterialId, Quantity = li.Quantity, UnitCost = rl.UnitCost, TaxRate = rl.TaxRate });
            }
            ret.Subtotal = ret.Lines.Sum(l => Money.Round(l.Quantity * l.UnitCost, s.DecimalPlaces));
            ret.TaxAmount = r.IsInvoiced ? ret.Lines.Sum(l => Money.Round(Money.Round(l.Quantity * l.UnitCost, s.DecimalPlaces) * l.TaxRate / 100m, s.DecimalPlaces)) : 0;
            ret.Total = ret.Subtotal + ret.TaxAmount;
            db.PurchaseReturns.Add(ret);
            await db.SaveChangesAsync();
            var tags = new LineTags(SupplierId: r.SupplierId);
            var draft = new JournalDraft { Date = ret.Date, Description = $"Purchase return {ret.Number} to {r.Supplier!.Name}", SourceType = "PurchaseReturn", SourceId = ret.Id, SourceNumber = ret.Number };
            if (r.IsInvoiced)
            {
                draft.Dr(SystemAccounts.AP, ret.Total, tags: tags).Cr(SystemAccounts.InputTax, ret.TaxAmount);
                var inv = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.ReceiptId == r.Id && i.Status == DocumentStatus.Posted);
                if (inv != null) inv.ReturnedAmount += ret.Total;
            }
            else draft.Dr(SystemAccounts.GRNI, ret.Subtotal, tags: tags);
            var txs = new List<InventoryTransaction>();
            foreach (var l in ret.Lines)
            {
                var m = await db.Materials.FirstAsync(x => x.Id == l.MaterialId);
                var tx = await InventoryEngine.IssueAtCostAsync(db, m, new InventoryEngine.Movement(InventoryTxType.PurchaseReturn, ret.Date, r.WarehouseId, l.Quantity, l.UnitCost, SourceType: "PurchaseReturn", SourceId: ret.Id, Reference: ret.Number));
                draft.Cr(AccountingEngine.InventoryAccountKey(m.Kind), -tx.TotalCost, $"{m.Code} × {l.Quantity:0.###}");
                var lineValue = Money.Round(l.Quantity * l.UnitCost, s.DecimalPlaces);
                var diff = lineValue - (-tx.TotalCost); // inventory held less value than the receipt price (stock already consumed at lower average)
                if (diff > 0) draft.Cr(SystemAccounts.InventoryGain, diff);
                else if (diff < 0) draft.Dr(SystemAccounts.InventoryLoss, -diff);
                txs.Add(tx);
            }
            var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
            foreach (var tx in txs) tx.JournalEntry = je;
            await db.SaveChangesAsync();
            ret.JournalEntryId = je?.Id;
            Audit(db, AuditAction.Posted, nameof(PurchaseReturn), ret.Id, ret.Number);
            return ret.Id;
        });
    }
}
