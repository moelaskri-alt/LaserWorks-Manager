using LaserWorks.Application.Abstractions;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

/// <summary>Simple role-based access control. Matrix is stored in the database and editable by administrators.</summary>
public sealed class PermissionService
{
    private readonly IAppDbFactory _factory;
    private readonly ICurrentUser _user;
    private Dictionary<(UserRole, AppModule), Permission>? _cache;

    public PermissionService(IAppDbFactory factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public static Dictionary<(UserRole, AppModule), Permission> DefaultMatrix()
    {
        var m = new Dictionary<(UserRole, AppModule), Permission>();
        const Permission A = Permission.All;
        const Permission V = Permission.View;
        const Permission VP = Permission.View | Permission.Print | Permission.Export;
        const Permission CRUD = Permission.View | Permission.Create | Permission.Edit | Permission.Print | Permission.Export;
        foreach (var mod in Enum.GetValues<AppModule>())
        {
            m[(UserRole.Administrator, mod)] = A;
            m[(UserRole.Manager, mod)] = mod == AppModule.Users ? V : mod == AppModule.Backup ? Permission.View | Permission.Create : A;
            m[(UserRole.Accountant, mod)] = mod switch
            {
                AppModule.Accounting or AppModule.Sales or AppModule.Purchases or AppModule.Expenses => A,
                AppModule.Customers => CRUD,
                AppModule.Reports or AppModule.Profitability or AppModule.Dashboard => VP,
                AppModule.Jobs or AppModule.Inventory or AppModule.Estimates or AppModule.Quotations or AppModule.Machines or AppModule.Employees => VP,
                AppModule.Backup => Permission.View | Permission.Create,
                _ => Permission.None
            };
            m[(UserRole.Sales, mod)] = mod switch
            {
                AppModule.Customers => CRUD,
                AppModule.Requests => CRUD,
                AppModule.Design => VP,
                AppModule.Estimates => CRUD,
                AppModule.Quotations => CRUD | Permission.Approve,
                AppModule.Jobs => VP | Permission.Create,
                AppModule.Sales => CRUD | Permission.Post,
                AppModule.Dashboard or AppModule.Reports or AppModule.Profitability => VP,
                AppModule.Inventory or AppModule.Machines => V,
                _ => Permission.None
            };
            m[(UserRole.Production, mod)] = mod switch
            {
                AppModule.Jobs => CRUD | Permission.Post,
                AppModule.Production => A & ~Permission.Delete,
                AppModule.Design => CRUD | Permission.Approve,
                AppModule.Inventory => CRUD | Permission.Post,
                AppModule.Machines or AppModule.Employees or AppModule.Requests or AppModule.Estimates => VP,
                AppModule.Dashboard or AppModule.Reports => VP,
                _ => Permission.None
            };
            m[(UserRole.Storekeeper, mod)] = mod switch
            {
                AppModule.Inventory => A & ~Permission.Delete,
                AppModule.Purchases => CRUD | Permission.Post,
                AppModule.Jobs or AppModule.Machines or AppModule.Dashboard or AppModule.Reports => VP,
                _ => Permission.None
            };
        }
        return m;
    }

    public async Task LoadAsync()
    {
        await using var db = _factory.Create();
        var rows = await db.RolePermissions.AsNoTracking().ToListAsync();
        var matrix = DefaultMatrix();
        foreach (var r in rows) matrix[(r.Role, r.Module)] = r.Permissions;
        _cache = matrix;
    }

    public void Invalidate() => _cache = null;

    public Permission For(UserRole role, AppModule module)
    {
        if (role == UserRole.Administrator) return Permission.All; // administrators can never lock themselves out
        _cache ??= LoadSync();
        return _cache.TryGetValue((role, module), out var p) ? p : Permission.None;
    }

    private Dictionary<(UserRole, AppModule), Permission> LoadSync()
    {
        LoadAsync().GetAwaiter().GetResult();
        return _cache!;
    }

    /// <summary>True when the current user holds the permission. Background/system operations (no signed-in user) are allowed.</summary>
    public bool Has(AppModule module, Permission permission)
    {
        if (_user.Role is not { } role) return true;
        return (For(role, module) & permission) == permission;
    }

    public void Demand(AppModule module, Permission permission)
    {
        if (!Has(module, permission)) throw new DomainException("Err.AccessDenied", module, permission);
    }

    public async Task SaveMatrixAsync(IEnumerable<RolePermission> rows)
    {
        Demand(AppModule.Users, Permission.Edit);
        await using var db = _factory.Create();
        var existing = await db.RolePermissions.ToListAsync();
        foreach (var r in rows)
        {
            if (r.Role == UserRole.Administrator) continue;
            var e = existing.FirstOrDefault(x => x.Role == r.Role && x.Module == r.Module);
            if (e == null) db.RolePermissions.Add(new RolePermission { Role = r.Role, Module = r.Module, Permissions = r.Permissions });
            else e.Permissions = r.Permissions;
        }
        db.AuditLogs.Add(new AuditLog { Timestamp = DateTime.Now, Username = _user.Username, Action = AuditAction.Updated, Entity = nameof(RolePermission) });
        await db.SaveChangesAsync();
        _cache = null;
    }
}
