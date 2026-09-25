using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public enum LoginResult { Success, InvalidCredentials, LockedOut, Inactive }

public sealed record UserRow(long Id, string Username, string FullName, UserRole Role, bool IsActive, DateTime? LastLoginAt, DateTime? LockoutUntil, int FailedLoginCount);

public sealed class AuthService : ServiceBase
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private readonly UserSession _session;

    public AuthService(ServiceContext ctx, UserSession session) : base(ctx) => _session = session;

    public async Task<bool> AnyUserAsync() => await ReadAsync(db => db.Users.AnyAsync());

    public async Task<(LoginResult Result, DateTime? LockedUntil)> LoginAsync(string username, string password)
    {
        await using var db = Factory.Create();
        var name = (username ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == name);
        if (user == null)
        {
            db.AuditLogs.Add(new AuditLog { Timestamp = Now, Username = name, Action = AuditAction.LoginFailed, Entity = nameof(User), Details = "unknown user" });
            await db.SaveChangesAsync();
            return (LoginResult.InvalidCredentials, null);
        }
        if (!user.IsActive) return (LoginResult.Inactive, null);
        if (user.LockoutUntil is { } until && until > Now) return (LoginResult.LockedOut, until);

        if (!PasswordHasher.Verify(password ?? "", user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutUntil = Now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }
            db.AuditLogs.Add(new AuditLog { Timestamp = Now, Username = name, Action = AuditAction.LoginFailed, Entity = nameof(User), RecordId = user.Id });
            await db.SaveChangesAsync();
            return user.LockoutUntil > Now ? (LoginResult.LockedOut, user.LockoutUntil) : (LoginResult.InvalidCredentials, null);
        }

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = Now;
        db.AuditLogs.Add(new AuditLog { Timestamp = Now, Username = name, Action = AuditAction.Login, Entity = nameof(User), RecordId = user.Id });
        await db.SaveChangesAsync();
        _session.SignIn(user.Id, user.Username, user.FullName, user.Role, Now);
        Ctx.Permissions.Invalidate();
        return (LoginResult.Success, null);
    }

    public async Task LogoutAsync()
    {
        if (!_session.IsAuthenticated) return;
        await using var db = Factory.Create();
        db.AuditLogs.Add(new AuditLog { Timestamp = Now, Username = _session.Username, Action = AuditAction.Logout, Entity = nameof(User), RecordId = _session.UserId });
        await db.SaveChangesAsync();
        _session.SignOut();
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        if (!_session.IsAuthenticated) throw new DomainException("Err.NotSignedIn");
        if (!PasswordHasher.MeetsPolicy(newPassword)) throw new DomainException("Err.PasswordPolicy");
        await using var db = Factory.Create();
        var user = await db.Users.FirstAsync(u => u.Id == _session.UserId);
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash)) throw new DomainException("Err.WrongPassword");
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
    }

    // ---- user administration ----

    public async Task<List<UserRow>> ListUsersAsync() => await ReadAsync(db => db.Users.AsNoTracking().OrderBy(u => u.Username)
        .Select(u => new UserRow(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.LastLoginAt, u.LockoutUntil, u.FailedLoginCount)).ToListAsync());

    public async Task<long> CreateUserAsync(string username, string fullName, UserRole role, string password, bool mustChange = false)
    {
        if (_session.IsAuthenticated) Demand(AppModule.Users, Permission.Create);
        var name = (username ?? "").Trim().ToLowerInvariant();
        if (name.Length < 3 || name.Any(char.IsWhiteSpace)) throw new DomainException("Err.UsernameInvalid");
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Err.Required", "FullName");
        if (!PasswordHasher.MeetsPolicy(password)) throw new DomainException("Err.PasswordPolicy");
        await using var db = Factory.Create();
        if (await db.Users.AnyAsync(u => u.Username == name)) throw new DomainException("Err.DuplicateUsername", name);
        var u = new User { Username = name, FullName = fullName.Trim(), Role = role, PasswordHash = PasswordHasher.Hash(password), MustChangePassword = mustChange };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    public async Task UpdateUserAsync(long id, string fullName, UserRole role, bool isActive)
    {
        Demand(AppModule.Users, Permission.Edit);
        await using var db = Factory.Create();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (u.Id == _session.UserId && (!isActive || role != UserRole.Administrator) && u.Role == UserRole.Administrator)
            throw new DomainException("Err.CannotDemoteSelf");
        if (u.Role == UserRole.Administrator && (role != UserRole.Administrator || !isActive))
        {
            var admins = await db.Users.CountAsync(x => x.Role == UserRole.Administrator && x.IsActive && x.Id != id);
            if (admins == 0) throw new DomainException("Err.LastAdministrator");
        }
        u.FullName = fullName.Trim();
        u.Role = role;
        u.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task ResetPasswordAsync(long id, string newPassword)
    {
        Demand(AppModule.Users, Permission.Edit);
        if (!PasswordHasher.MeetsPolicy(newPassword)) throw new DomainException("Err.PasswordPolicy");
        await using var db = Factory.Create();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        u.PasswordHash = PasswordHasher.Hash(newPassword);
        u.LockoutUntil = null;
        u.FailedLoginCount = 0;
        u.MustChangePassword = true;
        await db.SaveChangesAsync();
    }

    public async Task UnlockAsync(long id)
    {
        Demand(AppModule.Users, Permission.Edit);
        await using var db = Factory.Create();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        u.LockoutUntil = null;
        u.FailedLoginCount = 0;
        await db.SaveChangesAsync();
    }
}

public sealed record AuditRow(long Id, DateTime Timestamp, string Username, AuditAction Action, string Entity, long? RecordId, string? Details);

public sealed class AuditService : ServiceBase
{
    public AuditService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<AuditRow>> ListAsync(PageRequest req, DateTime? from = null, DateTime? to = null, string? entity = null, AuditAction? action = null)
    {
        Demand(AppModule.Users, Permission.View);
        await using var db = Factory.Create();
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (from.HasValue) q = q.Where(a => a.Timestamp >= from.Value.Date);
        if (to.HasValue) q = q.Where(a => a.Timestamp < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(entity)) q = q.Where(a => a.Entity == entity);
        if (action.HasValue) q = q.Where(a => a.Action == action.Value);
        if (req.Search.Norm() is { } s) q = q.Where(a => a.Username.Contains(s) || a.Entity.Contains(s) || (a.Details != null && a.Details.Contains(s)));
        return await q.OrderByDescending(a => a.Id)
            .Select(a => new AuditRow(a.Id, a.Timestamp, a.Username, a.Action, a.Entity, a.RecordId, a.Details)).ToPagedAsync(req);
    }
}
