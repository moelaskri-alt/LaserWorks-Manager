using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record RequestRow(long Id, string Number, DateTime RequestDate, long CustomerId, string Customer, string Description, string? FirstItem, int ItemCount, decimal Quantity,
    DateTime? RequiredDate, RequestStatus Status, int Revisions, int Attachments)
{
    /// <summary>"MDF 4mm" or "MDF 4mm +3".</summary>
    public string Items => ItemCount <= 1 ? FirstItem ?? "" : $"{FirstItem} +{ItemCount - 1}";
}

public sealed record AttachmentRow(long Id, string FileName, long SizeBytes, string? Kind, DateTime AddedAt, string? AddedBy, string StoredPath)
{
    public string FileType => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
}

public sealed class RequestService : ServiceBase
{
    private readonly IAttachmentStore _files;

    public RequestService(ServiceContext ctx, IAttachmentStore files) : base(ctx) => _files = files;

    public async Task<PagedResult<RequestRow>> ListAsync(PageRequest req, RequestStatus? status = null, long? customerId = null, DateRange? range = null)
    {
        Demand(AppModule.Requests, Permission.View);
        await using var db = Factory.Create();
        var q = db.CustomerRequests.AsNoTracking().AsQueryable();
        if (status.HasValue) q = q.Where(r => r.Status == status);
        if (customerId.HasValue) q = q.Where(r => r.CustomerId == customerId);
        if (range != null) q = q.Where(r => r.RequestDate >= range.From.Date && r.RequestDate < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(r => r.Number.Contains(s) || r.Description.Contains(s) || r.Customer!.Name.Contains(s));
        return await q.SortBy(req.SortBy, req.Descending, e => e.Id, defaultDesc: true).Select(r => new RequestRow(r.Id, r.Number, r.RequestDate, r.CustomerId, r.Customer!.Name, r.Description,
                r.Items.OrderBy(i => i.LineNo).Select(i => i.Material != null ? i.Material.Name : i.Description).FirstOrDefault(), r.Items.Count(), r.Quantity,
                r.RequiredDate, r.Status, db.DesignRevisions.Count(d => d.RequestId == r.Id),
                db.Attachments.Count(a => a.OwnerType == AttachmentOwner.Request && a.OwnerId == r.Id)))
            .ToPagedAsync(req);
    }

    public async Task<List<Lookup>> LookupAsync(long? customerId = null) => await ReadAsync(db => db.CustomerRequests.AsNoTracking()
        .Where(r => (customerId == null || r.CustomerId == customerId) && r.Status != RequestStatus.Rejected)
        .OrderByDescending(r => r.Id).Take(500).Select(r => new Lookup(r.Id, r.Number, r.Description)).ToListAsync());

    public async Task<CustomerRequest?> GetAsync(long id) => await ReadAsync(db => db.CustomerRequests.AsNoTracking().Include(r => r.Customer)
        .Include(r => r.Items.OrderBy(i => i.LineNo)).ThenInclude(i => i.Material).FirstOrDefaultAsync(r => r.Id == id));

    public async Task<long> SaveAsync(CustomerRequest input)
    {
        if (input.CustomerId == 0) throw new DomainException("Err.Required", "Customer");
        Validation.Required(input.Description, "Description");
        if (input.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        foreach (var i in input.Items)
        {
            if (i.Quantity < 0) throw new DomainException("Err.NegativeValue");
            if (i.MaterialId == null && string.IsNullOrWhiteSpace(i.Description)) throw new DomainException("Err.Required", "Description");
        }
        if (input.RequiredDate.HasValue && input.RequiredDate.Value.Date < input.RequestDate.Date) throw new DomainException("Err.DueBeforeStart");
        Demand(AppModule.Requests, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            CustomerRequest r;
            if (input.Id == 0)
            {
                r = new CustomerRequest { Number = await Numbering.NextAsync(db, SequenceKey.Request), Status = RequestStatus.New };
                db.CustomerRequests.Add(r);
            }
            else
            {
                r = await db.CustomerRequests.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (r.Status == RequestStatus.ConvertedToJob && r.CustomerId != input.CustomerId) throw new DomainException("Err.RequestLocked");
            }
            r.CustomerId = input.CustomerId; r.RequestDate = input.RequestDate.Date; r.Description = input.Description.Trim(); r.Dimensions = input.Dimensions.Norm();
            r.Quantity = input.Quantity; r.RequiredDate = input.RequiredDate; r.Notes = input.Notes.Norm();
            // requested items: replace the list (lines have no history of their own)
            db.RequestItems.RemoveRange(r.Items);
            r.Items = new();
            var materials = await db.Materials.AsNoTracking().Where(m => input.Items.Select(i => i.MaterialId).Contains(m.Id)).ToDictionaryAsync(m => m.Id);
            var no = 1;
            foreach (var i in input.Items)
            {
                var m = i.MaterialId is { } mid && materials.TryGetValue(mid, out var mm) ? mm : null;
                r.Items.Add(new RequestItem
                {
                    LineNo = no++, MaterialId = m?.Id, Category = m != null ? LaserWorks.Domain.Costing.ComponentRules.CategoryOf(m.Kind) : i.Category,
                    Description = i.Description.Norm(), Quantity = i.Quantity, Unit = i.Unit.Norm(), Notes = i.Notes.Norm()
                });
            }
            await db.SaveChangesAsync();
            return r.Id;
        });
    }

    public async Task SetStatusAsync(long id, RequestStatus status)
    {
        Demand(AppModule.Requests, Permission.Edit);
        await TxAsync(async db =>
        {
            var r = await db.CustomerRequests.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (r.Status == RequestStatus.ConvertedToJob) throw new DomainException("Err.RequestLocked");
            if (status == RequestStatus.ConvertedToJob) throw new DomainException("Err.UseConvertToJob");
            r.Status = status;
            Audit(db, AuditAction.StatusChanged, nameof(CustomerRequest), id, status.ToString());
        });
    }

    internal static async Task AdvanceStatusAsync(IAppDb db, long? requestId, RequestStatus status)
    {
        if (requestId is not { } id) return;
        var r = await db.CustomerRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || r.Status == RequestStatus.ConvertedToJob || r.Status == RequestStatus.Rejected && status != RequestStatus.ConvertedToJob) return;
        if (status > r.Status || status == RequestStatus.ConvertedToJob) r.Status = status;
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Requests, Permission.Delete);
        await using var db = Factory.Create();
        var r = await db.CustomerRequests.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (r.Status != RequestStatus.New && r.Status != RequestStatus.Rejected) throw new DomainException("Err.RequestLocked");
        if (await db.DesignRevisions.AnyAsync(d => d.RequestId == id) || await db.CostEstimates.AnyAsync(e => e.RequestId == id)) throw new DomainException("Err.InUseCannotDelete");
        var atts = await db.Attachments.Where(a => a.OwnerType == AttachmentOwner.Request && a.OwnerId == id).ToListAsync();
        db.Attachments.RemoveRange(atts);
        db.CustomerRequests.Remove(r);
        await Validation.SaveDeleteAsync(db);
        foreach (var a in atts) _files.Delete(a.StoredPath);
    }

    // ---- attachments (shared by requests, revisions, jobs, quotations)
    public async Task<List<AttachmentRow>> AttachmentsAsync(AttachmentOwner owner, long ownerId) => await ReadAsync(db => db.Attachments.AsNoTracking()
        .Where(a => a.OwnerType == owner && a.OwnerId == ownerId).OrderBy(a => a.Id)
        .Select(a => new AttachmentRow(a.Id, a.FileName, a.SizeBytes, a.Kind, a.AddedAt, a.AddedBy, a.StoredPath)).ToListAsync());

    public async Task<long> AddAttachmentAsync(AttachmentOwner owner, long ownerId, string sourceFile, string? kind = null)
    {
        if (!File.Exists(sourceFile)) throw new DomainException("Err.FileNotFound", sourceFile);
        var info = new FileInfo(sourceFile);
        if (info.Length > 200L * 1024 * 1024) throw new DomainException("Err.FileTooLarge");
        var (stored, size) = await _files.SaveAsync(sourceFile);
        await using var db = Factory.Create();
        var a = new Attachment { OwnerType = owner, OwnerId = ownerId, FileName = info.Name, StoredPath = stored, SizeBytes = size, Kind = kind, AddedAt = Now, AddedBy = UserName };
        db.Attachments.Add(a);
        Audit(db, AuditAction.Created, nameof(Attachment), null, $"{owner}#{ownerId}: {info.Name}");
        await db.SaveChangesAsync();
        return a.Id;
    }

    public async Task DeleteAttachmentAsync(long id)
    {
        await using var db = Factory.Create();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (a.OwnerType == AttachmentOwner.DesignRevision && await db.DesignRevisions.AnyAsync(d => d.Id == a.OwnerId && d.Status == RevisionStatus.Approved))
            throw new DomainException("Err.RevisionLocked");
        db.Attachments.Remove(a);
        Audit(db, AuditAction.Deleted, nameof(Attachment), id, a.FileName);
        await db.SaveChangesAsync();
        _files.Delete(a.StoredPath);
    }

    public string ResolveAttachment(string storedPath) => _files.Resolve(storedPath);
}

public sealed record RevisionRow(long Id, long RequestId, string RequestNumber, string Customer, int RevisionNo, string RevisionLabel, DateTime Date, string? Designer,
    decimal Width, decimal Height, string? Material, decimal Thickness, decimal CuttingLengthM, decimal EngravingAreaCm2, decimal EstimatedMachineMinutes, RevisionStatus Status, int Files)
{
    public string Dimensions => Width == 0 && Height == 0 ? "" : $"{Width:0.##} × {Height:0.##}";
}

public sealed class DesignService : ServiceBase
{
    public DesignService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<RevisionRow>> ListAsync(PageRequest req, long? requestId = null, RevisionStatus? status = null)
    {
        Demand(AppModule.Design, Permission.View);
        await using var db = Factory.Create();
        var q = db.DesignRevisions.AsNoTracking().AsQueryable();
        if (requestId.HasValue) q = q.Where(d => d.RequestId == requestId);
        if (status.HasValue) q = q.Where(d => d.Status == status);
        if (req.Search.Norm() is { } s) q = q.Where(d => d.Request!.Number.Contains(s) || d.Request.Customer!.Name.Contains(s) || d.RevisionLabel.Contains(s));
        return await q.OrderByDescending(d => d.RequestId).ThenByDescending(d => d.RevisionNo)
            .Select(d => new RevisionRow(d.Id, d.RequestId, d.Request!.Number, d.Request.Customer!.Name, d.RevisionNo, d.RevisionLabel, d.Date, d.Designer != null ? d.Designer.Name : null,
                d.Width, d.Height, d.Material != null ? d.Material.Name : null, d.Thickness, d.CuttingLengthM, d.EngravingAreaCm2, d.EstimatedMachineMinutes, d.Status,
                db.Attachments.Count(a => a.OwnerType == AttachmentOwner.DesignRevision && a.OwnerId == d.Id)))
            .ToPagedAsync(req);
    }

    public async Task<DesignRevision?> GetAsync(long id) => await ReadAsync(db => db.DesignRevisions.AsNoTracking().Include(d => d.Request).FirstOrDefaultAsync(d => d.Id == id));

    public async Task<DesignRevision?> ApprovedForRequestAsync(long requestId) => await ReadAsync(db => db.DesignRevisions.AsNoTracking()
        .FirstOrDefaultAsync(d => d.RequestId == requestId && d.Status == RevisionStatus.Approved));

    public async Task<long> SaveAsync(DesignRevision input)
    {
        if (input.RequestId == 0) throw new DomainException("Err.Required", "Request");
        foreach (var (v, n) in new[] { (input.Width, "Width"), (input.Height, "Height"), (input.Thickness, "Thickness"), (input.CuttingLengthM, "CuttingLength"),
                     (input.EngravingAreaCm2, "EngravingArea"), (input.EstimatedMachineMinutes, "MachineMinutes") })
            Validation.NonNegative(v, n);
        Demand(AppModule.Design, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            DesignRevision d;
            if (input.Id == 0)
            {
                var req = await db.CustomerRequests.FirstOrDefaultAsync(r => r.Id == input.RequestId) ?? throw new DomainException("Err.NotFound");
                var next = (await db.DesignRevisions.Where(x => x.RequestId == req.Id).MaxAsync(x => (int?)x.RevisionNo) ?? 0) + 1;
                d = new DesignRevision { RequestId = req.Id, RevisionNo = next, RevisionLabel = $"V{next}", Status = RevisionStatus.Draft };
                db.DesignRevisions.Add(d);
                await RequestService.AdvanceStatusAsync(db, req.Id, RequestStatus.Designing);
            }
            else
            {
                d = await db.DesignRevisions.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (d.Status is RevisionStatus.Approved or RevisionStatus.Superseded or RevisionStatus.Rejected) throw new DomainException("Err.RevisionLocked");
            }
            d.Date = input.Date == default ? Now.Date : input.Date.Date; d.DesignerId = input.DesignerId; d.Width = input.Width; d.Height = input.Height;
            d.MaterialId = input.MaterialId; d.Thickness = input.Thickness; d.CuttingLengthM = input.CuttingLengthM; d.EngravingAreaCm2 = input.EngravingAreaCm2;
            d.EstimatedMachineMinutes = input.EstimatedMachineMinutes; d.Notes = input.Notes.Norm();
            if (input.Status == RevisionStatus.UnderReview) d.Status = RevisionStatus.UnderReview;
            await db.SaveChangesAsync();
            return d.Id;
        });
    }

    /// <summary>Marks a revision as the single APPROVED/FINAL design. A previously approved revision becomes Superseded; history is never overwritten.</summary>
    public async Task ApproveAsync(long id)
    {
        Demand(AppModule.Design, Permission.Approve);
        await TxAsync(async db =>
        {
            var d = await db.DesignRevisions.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (d.Status is not (RevisionStatus.Draft or RevisionStatus.UnderReview)) throw new DomainException("Err.RevisionLocked");
            var current = await db.DesignRevisions.Where(x => x.RequestId == d.RequestId && x.Status == RevisionStatus.Approved).ToListAsync();
            foreach (var c in current) c.Status = RevisionStatus.Superseded;
            d.Status = RevisionStatus.Approved;
            d.ApprovedAt = Now;
            d.ApprovedBy = UserName;
            d.RevisionLabel = $"V{d.RevisionNo} FINAL";
            Audit(db, AuditAction.Approved, nameof(DesignRevision), d.Id, d.RevisionLabel);
        });
    }

    public async Task RejectAsync(long id)
    {
        Demand(AppModule.Design, Permission.Approve);
        await TxAsync(async db =>
        {
            var d = await db.DesignRevisions.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (d.Status is not (RevisionStatus.Draft or RevisionStatus.UnderReview)) throw new DomainException("Err.RevisionLocked");
            d.Status = RevisionStatus.Rejected;
            Audit(db, AuditAction.StatusChanged, nameof(DesignRevision), d.Id, "Rejected");
        });
    }
}
