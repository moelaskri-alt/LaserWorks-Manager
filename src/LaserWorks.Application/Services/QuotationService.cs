using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record QuotationRow(long Id, string Number, int VersionNo, DateTime Date, string Customer, string Description, decimal Quantity, decimal EstimatedCost,
    decimal NetAmount, decimal Total, DateTime ValidUntil, QuotationStatus Status, bool IsLatestVersion, string? JobNumber)
{
    public string DisplayNumber => $"{Number} v{VersionNo}";
    public decimal MarginPercent => NetAmount == 0 ? 0 : Money.Round((NetAmount - EstimatedCost) / NetAmount * 100m);
}

public sealed record JobCreationOptions(DateTime? DueDate = null, JobPriority Priority = JobPriority.Normal, long? MachineId = null, long? OperatorId = null, bool CreateOperationsFromEstimate = true);

public sealed class QuotationService : ServiceBase
{
    public QuotationService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<QuotationRow>> ListAsync(PageRequest req, QuotationStatus? status = null, long? customerId = null, bool latestOnly = true, DateRange? range = null)
    {
        Demand(AppModule.Quotations, Permission.View);
        await ExpireOverdueAsync();
        await using var db = Factory.Create();
        var q = db.Quotations.AsNoTracking().AsQueryable();
        if (latestOnly) q = q.Where(x => x.IsLatestVersion);
        if (status.HasValue) q = q.Where(x => x.Status == status);
        if (customerId.HasValue) q = q.Where(x => x.CustomerId == customerId);
        if (range != null) q = q.Where(x => x.Date >= range.From.Date && x.Date < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(x => x.Number.Contains(s) || x.Description.Contains(s) || x.Customer!.Name.Contains(s));
        return await q.SortBy(req.SortBy, req.Descending, e => e.Id, defaultDesc: true).Select(x => new QuotationRow(x.Id, x.Number, x.VersionNo, x.Date, x.Customer!.Name, x.Description, x.Quantity, x.EstimatedCost,
                x.SellingPrice - x.DiscountAmount, x.Total, x.ValidUntil, x.Status, x.IsLatestVersion, db.Jobs.Where(j => j.QuotationId == x.Id).Select(j => j.Number).FirstOrDefault()))
            .ToPagedAsync(req);
    }

    public async Task<List<QuotationRow>> VersionsAsync(long id)
    {
        await using var db = Factory.Create();
        var root = await db.Quotations.Where(x => x.Id == id).Select(x => x.RootQuotationId ?? x.Id).FirstAsync();
        return await db.Quotations.AsNoTracking().Where(x => x.Id == root || x.RootQuotationId == root).OrderBy(x => x.VersionNo)
            .Select(x => new QuotationRow(x.Id, x.Number, x.VersionNo, x.Date, x.Customer!.Name, x.Description, x.Quantity, x.EstimatedCost, x.SellingPrice - x.DiscountAmount, x.Total, x.ValidUntil, x.Status, x.IsLatestVersion, null))
            .ToListAsync();
    }

    public async Task<Quotation?> GetAsync(long id) => await ReadAsync(db => db.Quotations.AsNoTracking().Include(q => q.Customer).Include(q => q.Request).Include(q => q.Estimate).FirstOrDefaultAsync(q => q.Id == id));

    /// <summary>Marks sent quotations past their validity date as expired.</summary>
    public async Task<int> ExpireOverdueAsync()
    {
        await using var db = Factory.Create();
        var today = Now.Date;
        var list = await db.Quotations.Where(q => q.Status == QuotationStatus.Sent && q.ValidUntil < today).ToListAsync();
        foreach (var q in list) q.Status = QuotationStatus.Expired;
        if (list.Count > 0) await db.SaveChangesAsync();
        return list.Count;
    }

    /// <summary>Creates a draft quotation from an estimate. The estimate is finalised so the quoted cost basis cannot change afterwards.</summary>
    public async Task<long> CreateFromEstimateAsync(long estimateId)
    {
        Demand(AppModule.Quotations, Permission.Create);
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            var e = await db.CostEstimates.FirstOrDefaultAsync(x => x.Id == estimateId) ?? throw new DomainException("Err.NotFound");
            if (e.TotalCost <= 0) throw new DomainException("Err.EstimateEmpty");
            e.Status = EstimateStatus.Final;
            var customer = await db.Customers.FirstAsync(c => c.Id == e.CustomerId);
            var q = new Quotation
            {
                Number = await Numbering.NextAsync(db, SequenceKey.Quotation), VersionNo = 1, IsLatestVersion = true, CustomerId = e.CustomerId, RequestId = e.RequestId, EstimateId = e.Id,
                Date = Now.Date, Description = e.Description, Quantity = e.Quantity, EstimatedCost = e.TotalCost, SellingPrice = e.SellingPrice > 0 ? e.SellingPrice : e.SuggestedPrice,
                TaxRate = s.DefaultTaxRate, ValidUntil = Now.Date.AddDays(s.QuotationValidityDays), DeliveryDays = 7,
                PaymentTerms = customer.PaymentTermsDays > 0 ? $"{customer.PaymentTermsDays}" : s.DefaultPaymentTerms, Status = QuotationStatus.Draft
            };
            q.Recalculate(s.DecimalPlaces);
            db.Quotations.Add(q);
            await db.SaveChangesAsync();
            return q.Id;
        });
    }

    public async Task<long> SaveAsync(Quotation input)
    {
        if (input.CustomerId == 0) throw new DomainException("Err.Required", "Customer");
        Validation.Required(input.Description, "Description");
        if (input.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        Validation.NonNegative(input.SellingPrice, "SellingPrice");
        Validation.NonNegative(input.DiscountAmount, "Discount");
        if (input.TaxRate is < 0 or > 100) throw new DomainException("Err.PercentRange", "Tax");
        if (input.ValidUntil.Date < input.Date.Date) throw new DomainException("Err.ValidityBeforeDate");
        Demand(AppModule.Quotations, input.Id == 0 ? Permission.Create : Permission.Edit);
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            Quotation q;
            if (input.Id == 0)
            {
                q = new Quotation { Number = await Numbering.NextAsync(db, SequenceKey.Quotation), VersionNo = 1, IsLatestVersion = true, Status = QuotationStatus.Draft };
                db.Quotations.Add(q);
            }
            else
            {
                q = await db.Quotations.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (q.Status != QuotationStatus.Draft) throw new DomainException("Err.QuotationNotDraft");
            }
            q.CustomerId = input.CustomerId; q.RequestId = input.RequestId; q.EstimateId = input.EstimateId; q.Date = input.Date.Date; q.Description = input.Description.Trim();
            q.Quantity = input.Quantity; q.EstimatedCost = input.EstimatedCost; q.SellingPrice = input.SellingPrice; q.DiscountAmount = input.DiscountAmount; q.TaxRate = input.TaxRate;
            q.ValidUntil = input.ValidUntil.Date; q.DeliveryDays = input.DeliveryDays; q.PaymentTerms = input.PaymentTerms.Norm(); q.Notes = input.Notes.Norm();
            if (q.EstimateId is { } eid)
                q.EstimatedCost = await db.CostEstimates.Where(e => e.Id == eid).Select(e => e.TotalCost).FirstAsync();
            q.Recalculate(s.DecimalPlaces);
            await db.SaveChangesAsync();
            return q.Id;
        });
    }

    private async Task ChangeStatusAsync(long id, Func<Quotation, IAppDb, Task> apply, AuditAction action, Permission perm)
    {
        Demand(AppModule.Quotations, perm);
        await TxAsync(async db =>
        {
            var q = await db.Quotations.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (!q.IsLatestVersion) throw new DomainException("Err.QuotationOldVersion");
            await apply(q, db);
            Audit(db, action, nameof(Quotation), id, $"{q.Number} v{q.VersionNo} → {q.Status}");
        });
    }

    public Task MarkSentAsync(long id) => ChangeStatusAsync(id, async (q, db) =>
    {
        if (q.Status != QuotationStatus.Draft) throw new DomainException("Err.InvalidStatusTransition", q.Status, QuotationStatus.Sent);
        if (q.SellingPrice <= 0) throw new DomainException("Err.PriceRequired");
        q.Status = QuotationStatus.Sent;
        await RequestService.AdvanceStatusAsync(db, q.RequestId, RequestStatus.Quoted);
    }, AuditAction.StatusChanged, Permission.Edit);

    public Task ApproveAsync(long id) => ChangeStatusAsync(id, async (q, db) =>
    {
        if (q.Status is not (QuotationStatus.Sent or QuotationStatus.Draft)) throw new DomainException("Err.InvalidStatusTransition", q.Status, QuotationStatus.Approved);
        if (q.ValidUntil < Now.Date) throw new DomainException("Err.QuotationExpired");
        if (q.SellingPrice <= 0) throw new DomainException("Err.PriceRequired");
        q.Status = QuotationStatus.Approved;
        q.ApprovedAt = Now;
        await RequestService.AdvanceStatusAsync(db, q.RequestId, RequestStatus.Approved);
    }, AuditAction.Approved, Permission.Approve);

    public Task RejectAsync(long id) => ChangeStatusAsync(id, async (q, db) =>
    {
        if (q.Status is not (QuotationStatus.Sent or QuotationStatus.Draft)) throw new DomainException("Err.InvalidStatusTransition", q.Status, QuotationStatus.Rejected);
        q.Status = QuotationStatus.Rejected;
        await Task.CompletedTask;
    }, AuditAction.StatusChanged, Permission.Edit);

    /// <summary>Creates a new version (draft copy). The previous version is preserved unchanged except for its "latest" flag.</summary>
    public async Task<long> NewVersionAsync(long id)
    {
        Demand(AppModule.Quotations, Permission.Create);
        return await TxAsync(async db =>
        {
            var old = await db.Quotations.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (!old.IsLatestVersion) throw new DomainException("Err.QuotationOldVersion");
            if (old.Status == QuotationStatus.Approved && await db.Jobs.AnyAsync(j => j.QuotationId == old.Id)) throw new DomainException("Err.QuotationHasJob");
            var s = await SettingsAsync();
            var n = new Quotation
            {
                Number = old.Number, VersionNo = old.VersionNo + 1, RootQuotationId = old.RootQuotationId ?? old.Id, IsLatestVersion = true, CustomerId = old.CustomerId, RequestId = old.RequestId,
                EstimateId = old.EstimateId, Date = Now.Date, Description = old.Description, Quantity = old.Quantity, EstimatedCost = old.EstimatedCost, SellingPrice = old.SellingPrice,
                DiscountAmount = old.DiscountAmount, TaxRate = old.TaxRate, ValidUntil = Now.Date.AddDays(s.QuotationValidityDays), DeliveryDays = old.DeliveryDays,
                PaymentTerms = old.PaymentTerms, Notes = old.Notes, Status = QuotationStatus.Draft
            };
            n.Recalculate(s.DecimalPlaces);
            old.IsLatestVersion = false;
            if (old.Status is QuotationStatus.Draft or QuotationStatus.Sent or QuotationStatus.Approved) old.Status = QuotationStatus.Superseded;
            db.Quotations.Add(n);
            await db.SaveChangesAsync();
            return n.Id;
        });
    }

    /// <summary>Approved quotation → Job. Estimated cost and selling price are copied as snapshots.</summary>
    public async Task<long> CreateJobAsync(long quotationId, JobCreationOptions? options = null)
    {
        Demand(AppModule.Jobs, Permission.Create);
        options ??= new JobCreationOptions();
        return await TxAsync(async db =>
        {
            var q = await db.Quotations.FirstOrDefaultAsync(x => x.Id == quotationId) ?? throw new DomainException("Err.NotFound");
            if (q.Status != QuotationStatus.Approved) throw new DomainException("Err.QuotationNotApproved");
            if (await db.Jobs.AnyAsync(j => j.QuotationId == q.Id)) throw new DomainException("Err.QuotationHasJob");
            CostEstimate? est = q.EstimateId is { } eid
                ? await db.CostEstimates.Include(e => e.MachineLines).Include(e => e.LaborLines).FirstOrDefaultAsync(e => e.Id == eid)
                : null;
            long? revisionId = est?.DesignRevisionId;
            if (revisionId == null && q.RequestId is { } rid)
                revisionId = await db.DesignRevisions.Where(d => d.RequestId == rid && d.Status == RevisionStatus.Approved).Select(d => (long?)d.Id).FirstOrDefaultAsync();
            var job = new Job
            {
                Number = await Numbering.NextAsync(db, SequenceKey.Job), CustomerId = q.CustomerId, RequestId = q.RequestId, QuotationId = q.Id, EstimateId = q.EstimateId,
                DesignRevisionId = revisionId, Title = q.Description.Length > 290 ? q.Description[..290] : q.Description, Description = q.Description, Quantity = q.Quantity,
                OrderDate = Now.Date, DueDate = options.DueDate ?? Now.Date.AddDays(Math.Max(1, q.DeliveryDays)), Priority = options.Priority, Status = JobStatus.New,
                EstimatedCost = q.EstimatedCost, SellingPrice = q.SellingPrice - q.DiscountAmount,
                MachineId = options.MachineId ?? est?.MachineLines.Select(m => (long?)m.MachineId).FirstOrDefault(), OperatorId = options.OperatorId
            };
            db.Jobs.Add(job);
            if (options.CreateOperationsFromEstimate && est != null)
            {
                int seq = 1;
                if (est.DesignHours > 0)
                    job.Operations.Add(new JobOperation { Sequence = seq++, OperationType = OperationType.Design, PlannedHours = est.DesignHours, Quantity = job.Quantity });
                if (est.SetupHours > 0)
                    job.Operations.Add(new JobOperation { Sequence = seq++, OperationType = OperationType.MaterialPreparation, PlannedHours = est.SetupHours, Quantity = job.Quantity, EmployeeId = options.OperatorId });
                foreach (var m in est.MachineLines)
                    job.Operations.Add(new JobOperation { Sequence = seq++, OperationType = m.Operation, MachineId = m.MachineId, EmployeeId = options.OperatorId, PlannedHours = m.Hours, Quantity = job.Quantity });
                foreach (var l in est.LaborLines)
                    job.Operations.Add(new JobOperation { Sequence = seq++, OperationType = l.Operation, EmployeeId = l.EmployeeId ?? options.OperatorId, PlannedHours = l.Hours, Quantity = job.Quantity });
            }
            await RequestService.AdvanceStatusAsync(db, q.RequestId, RequestStatus.ConvertedToJob);
            await db.SaveChangesAsync();
            Audit(db, AuditAction.Created, nameof(Job), job.Id, $"from quotation {q.Number} v{q.VersionNo}");
            return job.Id;
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Quotations, Permission.Delete);
        await using var db = Factory.Create();
        var q = await db.Quotations.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (q.Status != QuotationStatus.Draft || q.VersionNo > 1) throw new DomainException("Err.QuotationNotDraft");
        db.Quotations.Remove(q);
        await Validation.SaveDeleteAsync(db);
    }
}
