using LaserWorks.Application.Abstractions;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Tests.Support;

/// <summary>Reads posted general-ledger balances straight from the database.</summary>
public static class Ledger
{
    /// <summary>Debit − credit of all posted lines on the system account, optionally for one job / customer.</summary>
    public static async Task<decimal> BalanceAsync(TestDb t, string systemKey, long? jobId = null, long? customerId = null)
    {
        await using var db = t.Get<IAppDbFactory>().Create();
        var q = db.JournalLines.AsNoTracking().Where(l => l.Account!.SystemKey == systemKey && l.JournalEntry!.Status != JournalStatus.Draft);
        if (jobId != null) q = q.Where(l => l.JobId == jobId);
        if (customerId != null) q = q.Where(l => l.CustomerId == customerId);
        var rows = await q.Select(l => new { l.Debit, l.Credit }).ToListAsync();
        return rows.Sum(r => r.Debit - r.Credit);
    }

    public static async Task<(decimal Debit, decimal Credit)> TotalsAsync(TestDb t)
    {
        await using var db = t.Get<IAppDbFactory>().Create();
        var rows = await db.JournalLines.AsNoTracking().Where(l => l.JournalEntry!.Status != JournalStatus.Draft).Select(l => new { l.Debit, l.Credit }).ToListAsync();
        return (rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }

    public static async Task AssertBooksBalanceAsync(TestDb t)
    {
        var (dr, cr) = await TotalsAsync(t);
        Assert.Equal(dr, cr);
        var checks = await t.Get<LaserWorks.Application.Services.ReconciliationService>().RunAsync();
        foreach (var c in checks) Assert.True(c.Passed, $"{c.Name}: expected {c.Expected}, actual {c.Actual} {c.Details}");
    }
}
