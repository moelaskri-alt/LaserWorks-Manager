using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record MaterialRow(long Id, string Code, string Name, string? Category, MaterialKind Kind, string? MaterialType, decimal Thickness,
    decimal Length, decimal Width, string Unit, decimal QuantityOnHand, decimal AverageCost, decimal StockValue, decimal ReorderLevel, decimal MinimumStock, bool IsActive)
{
    public bool IsLow => IsActive && ReorderLevel > 0 && QuantityOnHand <= ReorderLevel;
}

public sealed record MaterialLookup(long Id, string Code, string Name, decimal Thickness, decimal Length, decimal Width, decimal AverageCost, string Unit, bool IsSheet, MaterialKind Kind, decimal QuantityOnHand, decimal SalesPrice)
{
    public string Display => $"{Code} - {Name}";
    public override string ToString() => Display;
}

public sealed class MaterialService : ServiceBase
{
    public MaterialService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<MaterialRow>> ListAsync(PageRequest req, long? categoryId = null, MaterialKind? kind = null, bool lowOnly = false)
    {
        Demand(AppModule.Inventory, Permission.View);
        await using var db = Factory.Create();
        var q = db.Materials.AsNoTracking().AsQueryable();
        if (categoryId.HasValue) q = q.Where(m => m.CategoryId == categoryId);
        if (kind.HasValue) q = q.Where(m => m.Kind == kind);
        if (lowOnly) q = q.Where(m => m.IsActive && m.ReorderLevel > 0 && m.QuantityOnHand <= m.ReorderLevel);
        if (req.Search.Norm() is { } s) q = q.Where(m => m.Name.Contains(s) || m.Code.Contains(s) || (m.MaterialType != null && m.MaterialType.Contains(s)));
        return await q.Select(m => new MaterialRow(m.Id, m.Code, m.Name, m.Category != null ? m.Category.Name : null, m.Kind, m.MaterialType, m.Thickness, m.Length, m.Width,
                m.Unit!.Code, m.QuantityOnHand, m.AverageCost, m.StockValue, m.ReorderLevel, m.MinimumStock, m.IsActive))
            .SortBy(req.SortBy, req.Descending, r => r.Code).ToPagedAsync(req);
    }

    public async Task<List<MaterialLookup>> LookupAsync(MaterialKind? kind = null) => await ReadAsync(db => db.Materials.AsNoTracking()
        .Where(m => m.IsActive && (kind == null || m.Kind == kind)).OrderBy(m => m.Name)
        .Select(m => new MaterialLookup(m.Id, m.Code, m.Name, m.Thickness, m.Length, m.Width, m.AverageCost > 0 ? m.AverageCost : m.PurchaseCost, m.Unit!.Code, m.Unit.IsSheet, m.Kind, m.QuantityOnHand, m.SalesPrice))
        .ToListAsync());

    public async Task<Material?> GetAsync(long id) => await ReadAsync(db => db.Materials.AsNoTracking().Include(m => m.Unit).FirstOrDefaultAsync(c => c.Id == id));

    public async Task<long> SaveAsync(Material input)
    {
        Validation.Required(input.Name, "Name");
        if (input.UnitId == 0) throw new DomainException("Err.Required", "Unit");
        foreach (var (v, n) in new[] { (input.Thickness, "Thickness"), (input.Length, "Length"), (input.Width, "Width"), (input.PurchaseCost, "PurchaseCost"),
                     (input.MinimumStock, "MinimumStock"), (input.ReorderLevel, "ReorderLevel"), (input.SalesPrice, "SalesPrice") })
            Validation.NonNegative(v, n);
        Demand(AppModule.Inventory, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            Material m;
            if (input.Id == 0)
            {
                m = new Material { Code = string.IsNullOrWhiteSpace(input.Code) ? await Numbering.NextAsync(db, SequenceKey.Material) : input.Code.Trim() };
                db.Materials.Add(m);
            }
            else
            {
                m = await db.Materials.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (!string.IsNullOrWhiteSpace(input.Code)) m.Code = input.Code.Trim();
                if (m.Kind != input.Kind && (m.QuantityOnHand != 0 || m.StockValue != 0)) throw new DomainException("Err.KindChangeWithStock");
            }
            if (await db.Materials.AnyAsync(x => x.Code == m.Code && x.Id != input.Id)) throw new DomainException("Err.DuplicateCode", m.Code);
            if (!await db.Units.AnyAsync(u => u.Id == input.UnitId)) throw new DomainException("Err.NotFound");
            // stock quantities, value and average cost are only changed by inventory transactions
            m.Name = input.Name.Trim(); m.CategoryId = input.CategoryId; m.Kind = input.Kind; m.MaterialType = input.MaterialType.Norm();
            m.Thickness = input.Thickness; m.Length = input.Length; m.Width = input.Width; m.UnitId = input.UnitId; m.PurchaseCost = input.PurchaseCost;
            m.SupplierId = input.SupplierId; m.MinimumStock = input.MinimumStock; m.ReorderLevel = input.ReorderLevel; m.IsActive = input.IsActive;
            m.Notes = input.Notes.Norm(); m.SalesPrice = input.SalesPrice;
            await db.SaveChangesAsync();
            return m.Id;
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Inventory, Permission.Delete);
        await using var db = Factory.Create();
        var m = await db.Materials.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (m.QuantityOnHand != 0 || await db.InventoryTransactions.AnyAsync(t => t.MaterialId == id)) throw new DomainException("Err.InUseCannotDelete");
        var balances = await db.StockBalances.Where(b => b.MaterialId == id).ToListAsync();
        foreach (var b in balances) db.StockBalances.Remove(b);
        db.Materials.Remove(m);
        await Validation.SaveDeleteAsync(db);
    }

    public async Task<List<(string Warehouse, decimal Quantity)>> StockByWarehouseAsync(long materialId) => await ReadAsync(async db =>
        (await db.StockBalances.AsNoTracking().Where(b => b.MaterialId == materialId && b.Quantity != 0).Select(b => new { b.Warehouse!.Name, b.Quantity }).ToListAsync())
        .Select(x => (x.Name, x.Quantity)).ToList());

    public async Task<decimal> StockInWarehouseAsync(long materialId, long warehouseId) => await ReadAsync(db =>
        db.StockBalances.Where(b => b.MaterialId == materialId && b.WarehouseId == warehouseId).Select(b => b.Quantity).FirstOrDefaultAsync());
}

/// <summary>Small reference tables: warehouses, units, categories, cost centers, expense categories, laser parameters.</summary>
public sealed class LookupService : ServiceBase
{
    public LookupService(ServiceContext ctx) : base(ctx) { }

    public async Task<List<Warehouse>> WarehousesAsync(bool activeOnly = false) => await ReadAsync(db => db.Warehouses.AsNoTracking().Where(w => !activeOnly || w.IsActive).OrderBy(w => w.Code).ToListAsync());
    public async Task<List<UnitOfMeasure>> UnitsAsync() => await ReadAsync(db => db.Units.AsNoTracking().OrderBy(w => w.Id).ToListAsync());
    public async Task<List<MaterialCategory>> CategoriesAsync() => await ReadAsync(db => db.MaterialCategories.AsNoTracking().OrderBy(w => w.Name).ToListAsync());
    public async Task<List<CostCenter>> CostCentersAsync() => await ReadAsync(db => db.CostCenters.AsNoTracking().OrderBy(w => w.Code).ToListAsync());
    public async Task<List<ExpenseCategory>> ExpenseCategoriesAsync() => await ReadAsync(db => db.ExpenseCategories.AsNoTracking().Include(c => c.Account).OrderBy(w => w.Name).ToListAsync());

    public async Task<long> SaveWarehouseAsync(Warehouse w)
    {
        Validation.Required(w.Code, "Code"); Validation.Required(w.Name, "Name");
        Demand(AppModule.Settings, Permission.Edit);
        await using var db = Factory.Create();
        if (await db.Warehouses.AnyAsync(x => x.Code == w.Code.Trim() && x.Id != w.Id)) throw new DomainException("Err.DuplicateCode", w.Code);
        var row = w.Id == 0 ? db.Warehouses.Add(new Warehouse()).Entity : await db.Warehouses.FirstAsync(x => x.Id == w.Id);
        row.Code = w.Code.Trim(); row.Name = w.Name.Trim(); row.IsActive = w.IsActive;
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<long> SaveUnitAsync(UnitOfMeasure u)
    {
        Validation.Required(u.Code, "Code"); Validation.Required(u.NameEn, "Name");
        Demand(AppModule.Settings, Permission.Edit);
        await using var db = Factory.Create();
        var code = u.Code.Trim().ToUpperInvariant();
        if (await db.Units.AnyAsync(x => x.Code == code && x.Id != u.Id)) throw new DomainException("Err.DuplicateCode", code);
        var row = u.Id == 0 ? db.Units.Add(new UnitOfMeasure()).Entity : await db.Units.FirstAsync(x => x.Id == u.Id);
        if (row.IsSystem && row.Code != code) throw new DomainException("Err.SystemRecord");
        row.Code = code; row.NameEn = u.NameEn.Trim(); row.NameAr = string.IsNullOrWhiteSpace(u.NameAr) ? u.NameEn.Trim() : u.NameAr.Trim(); row.IsSheet = u.IsSheet;
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<long> SaveCategoryAsync(MaterialCategory c)
    {
        Validation.Required(c.Name, "Name");
        Demand(AppModule.Settings, Permission.Edit);
        await using var db = Factory.Create();
        if (await db.MaterialCategories.AnyAsync(x => x.Name == c.Name.Trim() && x.Id != c.Id)) throw new DomainException("Err.DuplicateCode", c.Name);
        var row = c.Id == 0 ? db.MaterialCategories.Add(new MaterialCategory()).Entity : await db.MaterialCategories.FirstAsync(x => x.Id == c.Id);
        row.Name = c.Name.Trim(); row.IsActive = c.IsActive;
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<long> SaveCostCenterAsync(CostCenter c)
    {
        Validation.Required(c.Code, "Code"); Validation.Required(c.Name, "Name");
        Demand(AppModule.Settings, Permission.Edit);
        await using var db = Factory.Create();
        if (await db.CostCenters.AnyAsync(x => x.Code == c.Code.Trim() && x.Id != c.Id)) throw new DomainException("Err.DuplicateCode", c.Code);
        var row = c.Id == 0 ? db.CostCenters.Add(new CostCenter()).Entity : await db.CostCenters.FirstAsync(x => x.Id == c.Id);
        row.Code = c.Code.Trim(); row.Name = c.Name.Trim(); row.IsActive = c.IsActive;
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<long> SaveExpenseCategoryAsync(ExpenseCategory c)
    {
        Validation.Required(c.Name, "Name");
        Demand(AppModule.Settings, Permission.Edit);
        await using var db = Factory.Create();
        var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == c.AccountId) ?? throw new DomainException("Err.Required", "Account");
        if (acc.Type != AccountType.Expense || !acc.IsPostable) throw new DomainException("Err.ExpenseAccountRequired");
        if (await db.ExpenseCategories.AnyAsync(x => x.Name == c.Name.Trim() && x.Id != c.Id)) throw new DomainException("Err.DuplicateCode", c.Name);
        var row = c.Id == 0 ? db.ExpenseCategories.Add(new ExpenseCategory()).Entity : await db.ExpenseCategories.FirstAsync(x => x.Id == c.Id);
        row.Name = c.Name.Trim(); row.AccountId = c.AccountId; row.JobComponent = c.JobComponent; row.IsActive = c.IsActive;
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task DeleteAsync<T>(long id) where T : Entity
    {
        Demand(AppModule.Settings, Permission.Delete);
        await using var db = Factory.Create();
        var set = ((Microsoft.EntityFrameworkCore.DbContext)db).Set<T>();
        var row = await set.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (row is UnitOfMeasure { IsSystem: true }) throw new DomainException("Err.SystemRecord");
        set.Remove(row);
        await Validation.SaveDeleteAsync(db);
    }

    // ---- laser parameter library (reference data only; never sent to a machine)
    public sealed record LaserParameterRow(long Id, string Material, string? Machine, decimal Thickness, OperationType Operation, decimal PowerPercent, decimal Speed, decimal? Frequency, int Passes, string? Notes);

    public async Task<List<LaserParameterRow>> LaserParametersAsync(string? search = null) => await ReadAsync(async db =>
    {
        var q = db.LaserParameters.AsNoTracking().AsQueryable();
        if (search.Norm() is { } s) q = q.Where(p => p.Material!.Name.Contains(s) || (p.Notes != null && p.Notes.Contains(s)));
        return await q.OrderBy(p => p.Material!.Name).ThenBy(p => p.Thickness)
            .Select(p => new LaserParameterRow(p.Id, p.Material!.Name, p.Machine != null ? p.Machine.Name : null, p.Thickness, p.OperationType, p.PowerPercent, p.SpeedMmPerSec, p.FrequencyHz, p.PassCount, p.Notes))
            .ToListAsync();
    });

    public async Task<LaserParameter?> GetLaserParameterAsync(long id) => await ReadAsync(db => db.LaserParameters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id));

    public async Task<long> SaveLaserParameterAsync(LaserParameter p)
    {
        if (p.MaterialId == 0) throw new DomainException("Err.Required", "Material");
        if (p.PowerPercent is < 0 or > 100) throw new DomainException("Err.PercentRange", "Power");
        if (p.SpeedMmPerSec <= 0) throw new DomainException("Err.Positive", "Speed");
        if (p.PassCount < 1) throw new DomainException("Err.Positive", "Passes");
        Demand(AppModule.Machines, p.Id == 0 ? Permission.Create : Permission.Edit);
        await using var db = Factory.Create();
        var row = p.Id == 0 ? db.LaserParameters.Add(new LaserParameter()).Entity : await db.LaserParameters.FirstAsync(x => x.Id == p.Id);
        row.MaterialId = p.MaterialId; row.MachineId = p.MachineId; row.Thickness = p.Thickness; row.OperationType = p.OperationType; row.PowerPercent = p.PowerPercent;
        row.SpeedMmPerSec = p.SpeedMmPerSec; row.FrequencyHz = p.FrequencyHz; row.PassCount = p.PassCount; row.Notes = p.Notes.Norm();
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task DeleteLaserParameterAsync(long id)
    {
        Demand(AppModule.Machines, Permission.Delete);
        await using var db = Factory.Create();
        db.LaserParameters.Remove(await db.LaserParameters.FirstAsync(x => x.Id == id));
        await db.SaveChangesAsync();
    }
}
