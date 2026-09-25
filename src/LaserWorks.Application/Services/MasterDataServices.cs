using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

internal static class Validation
{
    public static void Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("Err.Required", field);
    }

    public static void NonNegative(decimal value, string field)
    {
        if (value < 0) throw new DomainException("Err.NonNegative", field);
    }

    public static void Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var v = value.Trim();
        var at = v.IndexOf('@');
        if (at < 1 || at != v.LastIndexOf('@') || v.IndexOf('.', at) < at + 2 || v.EndsWith('.')) throw new DomainException("Err.EmailInvalid");
    }

    public static async Task SaveDeleteAsync(IAppDb db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new DomainException("Err.InUseCannotDelete");
        }
    }
}

// ------------------------------------------------------------------ Customers
public sealed record CustomerRow(long Id, string Code, string Name, string? Phone, string? Email, string? TaxNumber, decimal CreditLimit, int PaymentTermsDays, bool IsActive, decimal Balance);

public sealed record CustomerProfile(
    Customer Customer, decimal Balance, int Requests, int Quotations, int Jobs, int OpenJobs, int Invoices,
    decimal TotalInvoiced, decimal TotalPaid, decimal Revenue, decimal Cost, decimal GrossProfit, decimal MarginPercent,
    List<(string Type, long Id, string Number, DateTime Date, string Status, decimal Amount)> Documents);

public sealed class CustomerService : ServiceBase
{
    public CustomerService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<CustomerRow>> ListAsync(PageRequest req, bool? active = null)
    {
        Demand(AppModule.Customers, Permission.View);
        await using var db = Factory.Create();
        var arId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AR)).Id;
        var q = db.Customers.AsNoTracking().AsQueryable();
        if (active.HasValue) q = q.Where(c => c.IsActive == active.Value);
        if (req.Search.Norm() is { } s) q = q.Where(c => c.Name.Contains(s) || c.Code.Contains(s) || (c.Phone != null && c.Phone.Contains(s)) || (c.Email != null && c.Email.Contains(s)));
        var rows = q.SortBy(req.SortBy, req.Descending, e => e.Code).Select(c => new CustomerRow(c.Id, c.Code, c.Name, c.Phone, c.Email, c.TaxNumber, c.CreditLimit, c.PaymentTermsDays, c.IsActive,
            db.JournalLines.Where(l => l.AccountId == arId && l.CustomerId == c.Id).Sum(l => l.Debit - l.Credit)));
        return await rows.ToPagedAsync(req);
    }

    public async Task<List<Lookup>> LookupAsync(bool activeOnly = true) => await ReadAsync(db => db.Customers.AsNoTracking()
        .Where(c => !activeOnly || c.IsActive).OrderBy(c => c.Name).Select(c => new Lookup(c.Id, c.Code, c.Name)).ToListAsync());

    public async Task<Customer?> GetAsync(long id) => await ReadAsync(db => db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id));

    public async Task<long> SaveAsync(Customer input)
    {
        Validation.Required(input.Name, "Name");
        Validation.Email(input.Email);
        Validation.NonNegative(input.CreditLimit, "CreditLimit");
        if (input.PaymentTermsDays < 0) throw new DomainException("Err.NonNegative", "PaymentTerms");
        Demand(AppModule.Customers, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            Customer c;
            if (input.Id == 0)
            {
                c = new Customer();
                c.Code = string.IsNullOrWhiteSpace(input.Code) ? await Numbering.NextAsync(db, SequenceKey.Customer) : input.Code.Trim();
                db.Customers.Add(c);
            }
            else c = await db.Customers.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
            if (input.Id != 0 && !string.IsNullOrWhiteSpace(input.Code)) c.Code = input.Code.Trim();
            if (await db.Customers.AnyAsync(x => x.Code == c.Code && x.Id != input.Id)) throw new DomainException("Err.DuplicateCode", c.Code);
            c.Name = input.Name.Trim(); c.Phone = input.Phone.Norm(); c.Email = input.Email.Norm(); c.Address = input.Address.Norm();
            c.TaxNumber = input.TaxNumber.Norm(); c.Notes = input.Notes.Norm(); c.CreditLimit = input.CreditLimit; c.PaymentTermsDays = input.PaymentTermsDays; c.IsActive = input.IsActive;
            await db.SaveChangesAsync();
            return c.Id;
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Customers, Permission.Delete);
        await using var db = Factory.Create();
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        db.Customers.Remove(c);
        await Validation.SaveDeleteAsync(db);
    }

    public async Task<decimal> BalanceAsync(long customerId)
    {
        await using var db = Factory.Create();
        var arId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AR)).Id;
        return await db.JournalLines.Where(l => l.AccountId == arId && l.CustomerId == customerId).SumAsync(l => l.Debit - l.Credit);
    }

    public async Task<CustomerProfile> ProfileAsync(long id)
    {
        Demand(AppModule.Customers, Permission.View);
        await using var db = Factory.Create();
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        var arId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AR)).Id;
        var balance = await db.JournalLines.Where(l => l.AccountId == arId && l.CustomerId == id).SumAsync(l => l.Debit - l.Credit);
        var docs = new List<(string, long, string, DateTime, string, decimal)>();
        docs.AddRange((await db.CustomerRequests.AsNoTracking().Where(r => r.CustomerId == id).Select(r => new { r.Id, r.Number, r.RequestDate, r.Status }).ToListAsync())
            .Select(r => ("Request", r.Id, r.Number, r.RequestDate, r.Status.ToString(), 0m)));
        docs.AddRange((await db.Quotations.AsNoTracking().Where(r => r.CustomerId == id && r.IsLatestVersion).Select(r => new { r.Id, r.Number, r.VersionNo, r.Date, r.Status, r.Total }).ToListAsync())
            .Select(r => ("Quotation", r.Id, $"{r.Number}/v{r.VersionNo}", r.Date, r.Status.ToString(), r.Total)));
        var jobs = await db.Jobs.AsNoTracking().Where(r => r.CustomerId == id).Select(r => new { r.Id, r.Number, r.OrderDate, r.Status, r.SellingPrice, r.ActualCost, r.InvoicedRevenue, r.CostTransferredToCogs }).ToListAsync();
        docs.AddRange(jobs.Select(r => ("Job", r.Id, r.Number, r.OrderDate, r.Status.ToString(), r.SellingPrice)));
        var invoices = await db.SalesInvoices.AsNoTracking().Where(r => r.CustomerId == id && r.Status == DocumentStatus.Posted).Select(r => new { r.Id, r.Number, r.Date, r.Total, r.PaidAmount, r.ReturnedAmount, r.Subtotal, r.DiscountAmount, r.CogsAmount }).ToListAsync();
        docs.AddRange(invoices.Select(r => ("Invoice", r.Id, r.Number, r.Date, r.Total - r.PaidAmount - r.ReturnedAmount == 0 ? "Paid" : "Open", r.Total)));
        docs.AddRange((await db.CustomerPayments.AsNoTracking().Where(r => r.CustomerId == id).Select(r => new { r.Id, r.Number, r.Date, r.Amount, r.Method }).ToListAsync())
            .Select(r => ("Payment", r.Id, r.Number, r.Date, r.Method.ToString(), r.Amount)));
        var returns = await db.SalesReturns.AsNoTracking().Where(r => r.CustomerId == id && r.Status == DocumentStatus.Posted).Select(r => new { r.Subtotal, r.CogsReversed }).ToListAsync();
        var revenue = invoices.Sum(i => i.Subtotal - i.DiscountAmount) - returns.Sum(r => r.Subtotal);
        var cost = invoices.Sum(i => i.CogsAmount) - returns.Sum(r => r.CogsReversed);
        var gp = revenue - cost;
        return new CustomerProfile(c, balance, docs.Count(d => d.Item1 == "Request"), docs.Count(d => d.Item1 == "Quotation"), jobs.Count,
            jobs.Count(j => j.Status < JobStatus.Delivered), invoices.Count, invoices.Sum(i => i.Total), invoices.Sum(i => i.PaidAmount), revenue, cost, gp,
            Money.Percent(gp, revenue), docs.OrderByDescending(d => d.Item4).ToList());
    }
}

// ------------------------------------------------------------------ Suppliers
public sealed record SupplierRow(long Id, string Code, string Name, string? Phone, string? Email, int PaymentTermsDays, bool IsActive, decimal Balance);

public sealed class SupplierService : ServiceBase
{
    public SupplierService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<SupplierRow>> ListAsync(PageRequest req)
    {
        Demand(AppModule.Purchases, Permission.View);
        await using var db = Factory.Create();
        var apId = (await AccountingEngine.AccountAsync(db, SystemAccounts.AP)).Id;
        var q = db.Suppliers.AsNoTracking().AsQueryable();
        if (req.Search.Norm() is { } s) q = q.Where(c => c.Name.Contains(s) || c.Code.Contains(s) || (c.Phone != null && c.Phone.Contains(s)));
        return await q.SortBy(req.SortBy, req.Descending, e => e.Code).Select(c => new SupplierRow(c.Id, c.Code, c.Name, c.Phone, c.Email, c.PaymentTermsDays, c.IsActive,
                db.JournalLines.Where(l => l.AccountId == apId && l.SupplierId == c.Id).Sum(l => l.Credit - l.Debit)))
            .ToPagedAsync(req);
    }

    public async Task<List<Lookup>> LookupAsync() => await ReadAsync(db => db.Suppliers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new Lookup(c.Id, c.Code, c.Name)).ToListAsync());

    public async Task<Supplier?> GetAsync(long id) => await ReadAsync(db => db.Suppliers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id));

    public async Task<long> SaveAsync(Supplier input)
    {
        Validation.Required(input.Name, "Name");
        Validation.Email(input.Email);
        Demand(AppModule.Purchases, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            Supplier c;
            if (input.Id == 0)
            {
                c = new Supplier { Code = string.IsNullOrWhiteSpace(input.Code) ? await Numbering.NextAsync(db, SequenceKey.Supplier) : input.Code.Trim() };
                db.Suppliers.Add(c);
            }
            else
            {
                c = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (!string.IsNullOrWhiteSpace(input.Code)) c.Code = input.Code.Trim();
            }
            if (await db.Suppliers.AnyAsync(x => x.Code == c.Code && x.Id != input.Id)) throw new DomainException("Err.DuplicateCode", c.Code);
            c.Name = input.Name.Trim(); c.Phone = input.Phone.Norm(); c.Email = input.Email.Norm(); c.Address = input.Address.Norm();
            c.TaxNumber = input.TaxNumber.Norm(); c.PaymentTermsDays = input.PaymentTermsDays; c.Notes = input.Notes.Norm(); c.IsActive = input.IsActive;
            await db.SaveChangesAsync();
            return c.Id;
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Purchases, Permission.Delete);
        await using var db = Factory.Create();
        db.Suppliers.Remove(await db.Suppliers.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound"));
        await Validation.SaveDeleteAsync(db);
    }
}

// ------------------------------------------------------------------ Employees
public sealed record EmployeeRow(long Id, string Code, string Name, string? Role, decimal HourlyCost, string? Phone, bool IsActive);

public sealed class EmployeeService : ServiceBase
{
    public EmployeeService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<EmployeeRow>> ListAsync(PageRequest req)
    {
        Demand(AppModule.Employees, Permission.View);
        await using var db = Factory.Create();
        var q = db.Employees.AsNoTracking().AsQueryable();
        if (req.Search.Norm() is { } s) q = q.Where(c => c.Name.Contains(s) || c.Code.Contains(s) || (c.Role != null && c.Role.Contains(s)));
        return await q.SortBy(req.SortBy, req.Descending, e => e.Code).Select(e => new EmployeeRow(e.Id, e.Code, e.Name, e.Role, e.HourlyCost, e.Phone, e.IsActive)).ToPagedAsync(req);
    }

    public async Task<List<Lookup>> LookupAsync() => await ReadAsync(db => db.Employees.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new Lookup(c.Id, c.Code, c.Name)).ToListAsync());

    public async Task<Employee?> GetAsync(long id) => await ReadAsync(db => db.Employees.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id));

    public async Task<long> SaveAsync(Employee input)
    {
        Validation.Required(input.Name, "Name");
        Validation.NonNegative(input.HourlyCost, "HourlyCost");
        Demand(AppModule.Employees, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            Employee e;
            if (input.Id == 0)
            {
                e = new Employee { Code = string.IsNullOrWhiteSpace(input.Code) ? await Numbering.NextAsync(db, SequenceKey.Employee) : input.Code.Trim() };
                db.Employees.Add(e);
            }
            else
            {
                e = await db.Employees.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (!string.IsNullOrWhiteSpace(input.Code)) e.Code = input.Code.Trim();
            }
            if (await db.Employees.AnyAsync(x => x.Code == e.Code && x.Id != input.Id)) throw new DomainException("Err.DuplicateCode", e.Code);
            e.Name = input.Name.Trim(); e.Role = input.Role.Norm(); e.HourlyCost = input.HourlyCost; e.Phone = input.Phone.Norm(); e.IsActive = input.IsActive;
            await db.SaveChangesAsync();
            return e.Id;
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Employees, Permission.Delete);
        await using var db = Factory.Create();
        db.Employees.Remove(await db.Employees.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound"));
        await Validation.SaveDeleteAsync(db);
    }
}

// ------------------------------------------------------------------ Machines
public sealed record MachineRow(long Id, string Code, string Name, string? MachineType, decimal LaserPowerWatts, decimal HourlyRate, bool Overridden, bool IsActive);

public sealed class MachineService : ServiceBase
{
    public MachineService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<MachineRow>> ListAsync(PageRequest req)
    {
        Demand(AppModule.Machines, Permission.View);
        await using var db = Factory.Create();
        var q = db.Machines.AsNoTracking().AsQueryable();
        if (req.Search.Norm() is { } s) q = q.Where(c => c.Name.Contains(s) || c.Code.Contains(s) || (c.MachineType != null && c.MachineType.Contains(s)));
        var total = await q.CountAsync();
        var list = await q.OrderBy(m => m.Code).Skip(req.Skip).Take(req.PageSize).ToListAsync();
        var rows = list.Select(m => new MachineRow(m.Id, m.Code, m.Name, m.MachineType, m.LaserPowerWatts, SafeRate(m), m.UseHourlyCostOverride, m.IsActive)).ToList();
        return new PagedResult<MachineRow>(rows, total, req.Page, req.PageSize);
    }

    private static decimal SafeRate(Machine m)
    {
        try { return m.CalculateRate().EffectiveRate; } catch (DomainException) { return 0; }
    }

    public async Task<List<Lookup>> LookupAsync() => await ReadAsync(db => db.Machines.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new Lookup(c.Id, c.Code, c.Name)).ToListAsync());

    public async Task<Machine?> GetAsync(long id) => await ReadAsync(db => db.Machines.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id));

    public async Task<long> SaveAsync(Machine input)
    {
        Validation.Required(input.Name, "Name");
        foreach (var (v, n) in new[] { (input.PurchaseCost, "PurchaseCost"), (input.UsefulLifeYears, "UsefulLife"), (input.ResidualValue, "ResidualValue"), (input.AnnualWorkingHours, "AnnualHours"),
                     (input.ElectricalLoadKw, "Load"), (input.ElectricityPricePerKwh, "Electricity"), (input.MaintenanceCostPerYear, "Maintenance"), (input.OperatorCostPerHour, "Operator"),
                     (input.OtherOverheadPerYear, "Overhead"), (input.HourlyCostOverride, "Override") })
            Validation.NonNegative(v, n);
        if (!input.UseHourlyCostOverride && input.AnnualWorkingHours <= 0) throw new DomainException("Err.AnnualHoursRequired");
        input.CalculateRate(); // validates residual vs cost
        Demand(AppModule.Machines, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            Machine m;
            if (input.Id == 0)
            {
                m = new Machine { Code = string.IsNullOrWhiteSpace(input.Code) ? await Numbering.NextAsync(db, SequenceKey.Machine) : input.Code.Trim() };
                db.Machines.Add(m);
            }
            else
            {
                m = await db.Machines.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (!string.IsNullOrWhiteSpace(input.Code)) m.Code = input.Code.Trim();
            }
            if (await db.Machines.AnyAsync(x => x.Code == m.Code && x.Id != input.Id)) throw new DomainException("Err.DuplicateCode", m.Code);
            var code = m.Code; var id = m.Id; var createdAt = m.CreatedAt; var createdBy = m.CreatedBy;
            db.Entry(m).CurrentValues.SetValues(input);
            m.Id = id; m.Code = code; m.Name = input.Name.Trim(); m.CreatedAt = createdAt; m.CreatedBy = createdBy;
            await db.SaveChangesAsync();
            return m.Id;
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Machines, Permission.Delete);
        await using var db = Factory.Create();
        db.Machines.Remove(await db.Machines.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound"));
        await Validation.SaveDeleteAsync(db);
    }
}
