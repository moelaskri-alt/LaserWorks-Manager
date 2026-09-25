using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

/// <summary>Idempotent base data needed by every installation (no business data, no demo data).</summary>
public sealed class SeedService
{
    private readonly IAppDbFactory _factory;

    public SeedService(IAppDbFactory factory) => _factory = factory;

    private sealed record AccountSeed(string Code, string NameAr, string NameEn, AccountType Type, string? Parent, bool Postable, string? Key, bool CashBank = false);

    private static readonly AccountSeed[] Chart =
    {
        new("1000", "الأصول", "Assets", AccountType.Asset, null, false, null),
        new("1010", "الصندوق", "Cash on Hand", AccountType.Asset, "1000", true, SystemAccounts.Cash, true),
        new("1020", "البنك", "Bank", AccountType.Asset, "1000", true, SystemAccounts.Bank, true),
        new("1100", "ذمم العملاء", "Accounts Receivable", AccountType.Asset, "1000", true, SystemAccounts.AR),
        new("1200", "مخزون المواد الخام", "Inventory - Raw Materials", AccountType.Asset, "1000", true, SystemAccounts.Inventory),
        new("1210", "مخزون البواقي", "Inventory - Remnants", AccountType.Asset, "1000", true, SystemAccounts.InventoryRemnants),
        new("1220", "مخزون المنتجات التامة", "Inventory - Finished Goods", AccountType.Asset, "1000", true, SystemAccounts.InventoryFG),
        new("1230", "مخزون المستهلكات", "Inventory - Consumables", AccountType.Asset, "1000", true, SystemAccounts.InventoryConsumables),
        new("1300", "إنتاج تحت التشغيل", "Work in Progress", AccountType.Asset, "1000", true, SystemAccounts.WIP),
        new("1400", "ضريبة المدخلات", "Input Tax (VAT)", AccountType.Asset, "1000", true, SystemAccounts.InputTax),
        new("1500", "الآلات والمعدات", "Machinery & Equipment", AccountType.Asset, "1000", true, SystemAccounts.FixedAssets),
        new("1510", "مجمع الإهلاك", "Accumulated Depreciation", AccountType.Asset, "1000", true, SystemAccounts.AccumDepreciation),
        new("2000", "الخصوم", "Liabilities", AccountType.Liability, null, false, null),
        new("2100", "ذمم الموردين", "Accounts Payable", AccountType.Liability, "2000", true, SystemAccounts.AP),
        new("2150", "بضاعة مستلمة غير مفوترة", "Goods Received Not Invoiced", AccountType.Liability, "2000", true, SystemAccounts.GRNI),
        new("2200", "ضريبة المخرجات", "Output Tax (VAT)", AccountType.Liability, "2000", true, SystemAccounts.OutputTax),
        new("2300", "أجور مستحقة", "Accrued Wages", AccountType.Liability, "2000", true, SystemAccounts.AccruedWages),
        new("3000", "حقوق الملكية", "Equity", AccountType.Equity, null, false, null),
        new("3100", "رأس المال", "Owner's Capital", AccountType.Equity, "3000", true, SystemAccounts.Capital),
        new("3200", "الأرباح المحتجزة", "Retained Earnings", AccountType.Equity, "3000", true, SystemAccounts.RetainedEarnings),
        new("3900", "أرصدة افتتاحية", "Opening Balance Equity", AccountType.Equity, "3000", true, SystemAccounts.OpeningEquity),
        new("4000", "الإيرادات", "Revenue", AccountType.Revenue, null, false, null),
        new("4100", "المبيعات", "Sales Revenue", AccountType.Revenue, "4000", true, SystemAccounts.Sales),
        new("4200", "مردودات المبيعات", "Sales Returns", AccountType.Revenue, "4000", true, SystemAccounts.SalesReturns),
        new("4900", "إيرادات أخرى", "Other Income", AccountType.Revenue, "4000", true, SystemAccounts.OtherIncome),
        new("5000", "تكلفة المبيعات", "Cost of Sales", AccountType.Expense, null, false, null),
        new("5100", "تكلفة البضاعة المباعة", "Cost of Goods Sold", AccountType.Expense, "5000", true, SystemAccounts.COGS),
        new("5200", "خسائر المخزون والهالك", "Inventory Loss & Scrap", AccountType.Expense, "5000", true, SystemAccounts.InventoryLoss),
        new("5210", "أرباح تسوية المخزون", "Inventory Adjustment Gain", AccountType.Expense, "5000", true, SystemAccounts.InventoryGain),
        new("6000", "المصروفات التشغيلية", "Operating Expenses", AccountType.Expense, null, false, null),
        new("6100", "الرواتب والأجور", "Salaries & Wages", AccountType.Expense, "6000", true, SystemAccounts.Salaries),
        new("6200", "الكهرباء", "Electricity", AccountType.Expense, "6000", true, SystemAccounts.Electricity),
        new("6300", "الصيانة", "Maintenance", AccountType.Expense, "6000", true, SystemAccounts.Maintenance),
        new("6400", "الإيجار", "Rent", AccountType.Expense, "6000", true, SystemAccounts.Rent),
        new("6500", "النقل", "Transport", AccountType.Expense, "6000", true, SystemAccounts.Transport),
        new("6600", "التغليف", "Packaging", AccountType.Expense, "6000", true, SystemAccounts.Packaging),
        new("6700", "التسويق", "Marketing", AccountType.Expense, "6000", true, SystemAccounts.Marketing),
        new("6800", "الإهلاك", "Depreciation", AccountType.Expense, "6000", true, SystemAccounts.Depreciation),
        new("6900", "مصروفات أخرى", "Other Expenses", AccountType.Expense, "6000", true, SystemAccounts.OtherExpense),
        new("6950", "أجور محملة على الأوامر", "Labor Absorbed into Jobs", AccountType.Expense, "6000", true, SystemAccounts.LaborApplied),
        new("6960", "تكلفة آلات محملة على الأوامر", "Machine Cost Absorbed into Jobs", AccountType.Expense, "6000", true, SystemAccounts.MachineApplied),
        new("6970", "تكاليف غير مباشرة محملة", "Overhead Absorbed into Jobs", AccountType.Expense, "6000", true, SystemAccounts.OverheadApplied),
    };

    private static readonly (string Code, string Ar, string En, bool Sheet)[] UnitSeeds =
    {
        ("SHEET", "لوح", "Sheet", true), ("PCS", "قطعة", "Piece", false), ("M", "متر", "Meter", false), ("M2", "متر مربع", "Square Meter", false),
        ("KG", "كيلوجرام", "Kilogram", false), ("L", "لتر", "Liter", false), ("ROLL", "رول", "Roll", false), ("BOX", "علبة", "Box", false)
    };

    private static readonly (string Name, string AccountKey, CostComponent Component)[] ExpenseCategorySeeds =
    {
        ("Electricity", SystemAccounts.Electricity, CostComponent.OtherDirect),
        ("Maintenance", SystemAccounts.Maintenance, CostComponent.Maintenance),
        ("Rent", SystemAccounts.Rent, CostComponent.OtherDirect),
        ("Salaries", SystemAccounts.Salaries, CostComponent.Labor),
        ("Transport", SystemAccounts.Transport, CostComponent.OtherDirect),
        ("Packaging", SystemAccounts.Packaging, CostComponent.Packaging),
        ("Marketing", SystemAccounts.Marketing, CostComponent.OtherDirect),
        ("Finishing Supplies", SystemAccounts.OtherExpense, CostComponent.Finishing),
        ("Consumables", SystemAccounts.OtherExpense, CostComponent.Consumables),
        ("Other", SystemAccounts.OtherExpense, CostComponent.OtherDirect),
    };

    public async Task EnsureBaseDataAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await db.CompanySettings.AnyAsync(ct))
            db.CompanySettings.Add(new CompanySettings { CompanyName = "LaserWorks", SetupCompleted = false });

        var existingUnits = await db.Units.Select(u => u.Code).ToListAsync(ct);
        foreach (var u in UnitSeeds.Where(u => !existingUnits.Contains(u.Code)))
            db.Units.Add(new UnitOfMeasure { Code = u.Code, NameAr = u.Ar, NameEn = u.En, IsSystem = true, IsSheet = u.Sheet });

        var accounts = await db.Accounts.ToListAsync(ct);
        foreach (var a in Chart)
        {
            if (accounts.Any(x => x.Code == a.Code || (a.Key != null && x.SystemKey == a.Key))) continue;
            var acc = new Account { Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, Type = a.Type, IsPostable = a.Postable, IsSystem = true, SystemKey = a.Key, IsCashOrBank = a.CashBank };
            if (a.Parent != null) acc.Parent = accounts.First(x => x.Code == a.Parent);
            accounts.Add(acc);
            db.Accounts.Add(acc);
        }
        await db.SaveChangesAsync(ct);

        var seqs = await db.NumberSequences.Select(s => s.Key).ToListAsync(ct);
        foreach (var k in Enum.GetValues<SequenceKey>().Where(k => !seqs.Contains(k)))
            db.NumberSequences.Add(new NumberSequence { Key = k, Prefix = Numbering.DefaultPrefix(k), NextNumber = 1, Padding = 5 });

        if (!await db.RolePermissions.AnyAsync(ct))
            foreach (var kv in PermissionService.DefaultMatrix())
                db.RolePermissions.Add(new RolePermission { Role = kv.Key.Item1, Module = kv.Key.Item2, Permissions = kv.Value });

        var cats = await db.ExpenseCategories.Select(c => c.Name).ToListAsync(ct);
        foreach (var c in ExpenseCategorySeeds.Where(c => !cats.Contains(c.Name)))
            db.ExpenseCategories.Add(new ExpenseCategory { Name = c.Name, AccountId = accounts.First(a => a.SystemKey == c.AccountKey).Id, JobComponent = c.Component });

        if (!await db.Warehouses.AnyAsync(ct))
            db.Warehouses.Add(new Warehouse { Code = "MAIN", Name = "Main Warehouse" });
        if (!await db.CostCenters.AnyAsync(ct))
        {
            db.CostCenters.Add(new CostCenter { Code = "PROD", Name = "Production" });
            db.CostCenters.Add(new CostCenter { Code = "ADMIN", Name = "Administration" });
            db.CostCenters.Add(new CostCenter { Code = "SALES", Name = "Sales" });
        }
        if (!await db.MaterialCategories.AnyAsync(ct))
            foreach (var n in new[] { "Wood", "Acrylic & Plastic", "Leather & Fabric", "Paper & Cardboard", "Glass", "Consumables", "Finished Goods" })
                db.MaterialCategories.Add(new MaterialCategory { Name = n });

        await db.SaveChangesAsync(ct);
        var settings = await db.CompanySettings.OrderBy(s => s.Id).FirstAsync(ct);
        if (settings.DefaultWarehouseId == null)
        {
            settings.DefaultWarehouseId = await db.Warehouses.OrderBy(w => w.Id).Select(w => w.Id).FirstAsync(ct);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
