using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record ReconciliationCheck(string Name, bool Passed, decimal Expected, decimal Actual, string? Details = null)
{
    public decimal Difference => Actual - Expected;
}

/// <summary>Verifies the general ledger agrees with the operational sub-ledgers (inventory, remnants, WIP, job costs, AR/AP tags).</summary>
public sealed class ReconciliationService : ServiceBase
{
    public ReconciliationService(ServiceContext ctx) : base(ctx) { }

    public async Task<List<ReconciliationCheck>> RunAsync()
    {
        await using var db = Factory.Create();
        var checks = new List<ReconciliationCheck>();
        async Task<decimal> Gl(string key)
        {
            var id = (await AccountingEngine.AccountAsync(db, key)).Id;
            return await db.JournalLines.Where(l => l.AccountId == id && l.JournalEntry!.Status != JournalStatus.Draft).SumAsync(l => l.Debit - l.Credit);
        }

        var d = await db.JournalLines.Where(l => l.JournalEntry!.Status != JournalStatus.Draft).SumAsync(l => l.Debit);
        var c = await db.JournalLines.Where(l => l.JournalEntry!.Status != JournalStatus.Draft).SumAsync(l => l.Credit);
        checks.Add(new ReconciliationCheck("Ledger debits = credits", d == c, d, c));

        var unbalanced = await db.JournalEntries.Where(e => e.Status != JournalStatus.Draft && e.Lines.Sum(l => l.Debit) != e.Lines.Sum(l => l.Credit)).CountAsync();
        checks.Add(new ReconciliationCheck("Every posted entry balances", unbalanced == 0, 0, unbalanced));

        foreach (var kind in Enum.GetValues<MaterialKind>().Where(LaserWorks.Domain.Costing.ComponentRules.IsStockable))
        {
            var key = AccountingEngine.InventoryAccountKey(kind);
            var sub = await db.Materials.Where(m => m.Kind == kind).SumAsync(m => m.StockValue);
            var gl = await Gl(key);
            checks.Add(new ReconciliationCheck($"Inventory GL ({key}) = stock value ({kind})", gl == sub, sub, gl));
        }
        var remSub = await db.Remnants.Where(r => r.Status == RemnantStatus.Available).SumAsync(r => r.Cost);
        var remGl = await Gl(SystemAccounts.InventoryRemnants);
        checks.Add(new ReconciliationCheck("Remnant inventory GL = available remnants", remSub == remGl, remSub, remGl));

        var wipSub = await db.Jobs.SumAsync(j => j.ActualCost - j.CostTransferredToCogs);
        var wipGl = await Gl(SystemAccounts.WIP);
        checks.Add(new ReconciliationCheck("WIP GL = open job costs", wipSub == wipGl, wipSub, wipGl));

        var jobCache = await db.Jobs.Select(j => new { j.Id, j.ActualCost, Sum = db.JobCostEntries.Where(e => e.JobId == j.Id).Sum(e => e.Amount) }).Where(x => x.ActualCost != x.Sum).CountAsync();
        checks.Add(new ReconciliationCheck("Job actual cost = cost entries", jobCache == 0, 0, jobCache));

        var qtyMismatch = await db.Materials.Select(m => new { m.QuantityOnHand, Sum = db.StockBalances.Where(b => b.MaterialId == m.Id).Sum(b => b.Quantity) }).Where(x => x.QuantityOnHand != x.Sum).CountAsync();
        checks.Add(new ReconciliationCheck("Material quantity = warehouse balances", qtyMismatch == 0, 0, qtyMismatch));

        var txMismatch = await db.Materials.Select(m => new { m.QuantityOnHand, m.StockValue, Q = db.InventoryTransactions.Where(t => t.MaterialId == m.Id && t.RemnantId == null).Sum(t => t.Quantity), V = db.InventoryTransactions.Where(t => t.MaterialId == m.Id && t.RemnantId == null).Sum(t => t.TotalCost) })
            .Where(x => x.QuantityOnHand != x.Q || x.StockValue != x.V).CountAsync();
        checks.Add(new ReconciliationCheck("Material totals = inventory ledger", txMismatch == 0, 0, txMismatch));

        var arId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AR)).Id;
        var untaggedAr = await db.JournalLines.CountAsync(l => l.AccountId == arId && l.CustomerId == null && l.JournalEntry!.Status != JournalStatus.Draft);
        checks.Add(new ReconciliationCheck("All receivable lines identify a customer", untaggedAr == 0, 0, untaggedAr));
        var apId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AP)).Id;
        var untaggedAp = await db.JournalLines.CountAsync(l => l.AccountId == apId && l.SupplierId == null && l.JournalEntry!.Status != JournalStatus.Draft);
        checks.Add(new ReconciliationCheck("All payable lines identify a supplier", untaggedAp == 0, 0, untaggedAp));

        var invoicesAr = await db.SalesInvoices.Where(i => i.Status == DocumentStatus.Posted).SumAsync(i => i.Total - i.PaidAmount - i.ReturnedAmount);
        var unallocatedPayments = await db.CustomerPayments.Where(p => p.InvoiceId == null).SumAsync(p => p.Amount);
        var openingAr = await db.JournalLines.Where(l => l.AccountId == arId && l.JournalEntry!.SourceType == "Opening" && l.JournalEntry.Status != JournalStatus.Draft).SumAsync(l => l.Debit - l.Credit);
        var manualAr = await db.JournalLines.Where(l => l.AccountId == arId && l.JournalEntry!.SourceType == "Manual" && l.JournalEntry.Status != JournalStatus.Draft).SumAsync(l => l.Debit - l.Credit);
        var arGl = await Gl(SystemAccounts.AR);
        var arExpected = invoicesAr - unallocatedPayments + openingAr + manualAr;
        checks.Add(new ReconciliationCheck("Receivables GL = open invoices", arExpected == arGl, arExpected, arGl));
        return checks;
    }
}
