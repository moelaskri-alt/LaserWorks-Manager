using LaserWorks.Application.Abstractions;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed class SettingsService
{
    private readonly IAppDbFactory _factory;
    private CompanySettings? _cache;

    public SettingsService(IAppDbFactory factory) => _factory = factory;

    public event EventHandler? Changed;

    public async Task<CompanySettings> GetAsync()
    {
        if (_cache != null) return _cache;
        await using var db = _factory.Create();
        _cache = await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync() ?? new CompanySettings();
        return _cache;
    }

    public CompanySettings Current => _cache ?? GetAsync().GetAwaiter().GetResult();

    public void Invalidate() => _cache = null;

    public async Task SaveAsync(CompanySettings s)
    {
        Validate(s);
        await using var db = _factory.Create();
        var row = await db.CompanySettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (row == null)
        {
            s.Id = 0;
            db.CompanySettings.Add(s);
        }
        else
        {
            var id = row.Id; var createdAt = row.CreatedAt; var createdBy = row.CreatedBy; var wh = row.DefaultWarehouseId;
            s.Id = id;
            db.Entry(row).CurrentValues.SetValues(s);
            row.Id = id; row.CreatedAt = createdAt; row.CreatedBy = createdBy; row.DefaultWarehouseId = s.DefaultWarehouseId ?? wh;
        }
        await db.SaveChangesAsync();
        _cache = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static void Validate(CompanySettings s)
    {
        if (string.IsNullOrWhiteSpace(s.CompanyName)) throw new DomainException("Err.Required", "CompanyName");
        if (string.IsNullOrWhiteSpace(s.CurrencyCode)) throw new DomainException("Err.Required", "Currency");
        if (s.DecimalPlaces is < 0 or > 4) throw new DomainException("Err.DecimalPlacesRange");
        if (s.DefaultTaxRate is < 0 or > 100) throw new DomainException("Err.PercentRange", "Tax");
        if (s.DefaultMarginPercent is < 0 or >= 100) throw new DomainException("Err.MarginBelow100");
        if (s.MinimumMarginPercent is < 0 or >= 100) throw new DomainException("Err.MarginBelow100");
        if (s.OverheadRate < 0 || s.DefaultLaborRate < 0 || s.DefaultMarkupPercent < 0) throw new DomainException("Err.NegativeValue");
    }

    public async Task<List<NumberSequence>> GetSequencesAsync()
    {
        await using var db = _factory.Create();
        return await db.NumberSequences.AsNoTracking().OrderBy(s => s.Key).ToListAsync();
    }

    public async Task SaveSequencesAsync(IEnumerable<NumberSequence> seqs)
    {
        await using var db = _factory.Create();
        foreach (var s in seqs)
        {
            if (s.NextNumber < 1 || s.Padding is < 1 or > 10) throw new DomainException("Err.SequenceInvalid", s.Key);
            var row = await db.NumberSequences.FirstOrDefaultAsync(x => x.Key == s.Key);
            if (row == null) db.NumberSequences.Add(new NumberSequence { Key = s.Key, Prefix = s.Prefix, NextNumber = s.NextNumber, Padding = s.Padding });
            else
            {
                if (s.NextNumber < row.NextNumber) throw new DomainException("Err.SequenceCannotGoBack", s.Key);
                row.Prefix = s.Prefix; row.NextNumber = s.NextNumber; row.Padding = s.Padding;
            }
        }
        await db.SaveChangesAsync();
    }
}
