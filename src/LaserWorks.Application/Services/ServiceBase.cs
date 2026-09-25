using LaserWorks.Application.Abstractions;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Application.Services;

public sealed class ServiceContext
{
    public ServiceContext(IAppDbFactory factory, ICurrentUser user, IClock clock, PermissionService permissions, SettingsService settings)
    {
        Factory = factory; User = user; Clock = clock; Permissions = permissions; Settings = settings;
    }

    public IAppDbFactory Factory { get; }
    public ICurrentUser User { get; }
    public IClock Clock { get; }
    public PermissionService Permissions { get; }
    public SettingsService Settings { get; }
}

public abstract class ServiceBase
{
    protected ServiceBase(ServiceContext ctx) => Ctx = ctx;

    protected ServiceContext Ctx { get; }
    protected IAppDbFactory Factory => Ctx.Factory;
    protected IClock Clock => Ctx.Clock;
    protected string UserName => Ctx.User.Username;
    protected DateTime Now => Ctx.Clock.Now;

    protected void Demand(AppModule module, Permission permission) => Ctx.Permissions.Demand(module, permission);

    /// <summary>Runs work in a single database transaction; commits only if everything succeeds.</summary>
    protected async Task<T> TxAsync<T>(Func<IAppDb, Task<T>> work, CancellationToken ct = default)
    {
        await using var db = Factory.Create();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work(db);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    protected Task TxAsync(Func<IAppDb, Task> work, CancellationToken ct = default) =>
        TxAsync<bool>(async db => { await work(db); return true; }, ct);

    protected async Task<T> ReadAsync<T>(Func<IAppDb, Task<T>> work)
    {
        await using var db = Factory.Create();
        return await work(db);
    }

    protected void Audit(IAppDb db, AuditAction action, string entity, long? id, string? details = null) =>
        db.AuditLogs.Add(new AuditLog { Timestamp = Now, Username = UserName, Action = action, Entity = entity, RecordId = id, Details = details });

    protected async Task<CompanySettings> SettingsAsync() => await Ctx.Settings.GetAsync();
}
