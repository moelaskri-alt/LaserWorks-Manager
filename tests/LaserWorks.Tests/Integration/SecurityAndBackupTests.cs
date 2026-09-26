using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Infrastructure.Backup;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Tests.Integration;

public class SecurityTests
{
    [Fact]
    public async Task Passwords_are_stored_hashed()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Get<IAppDbFactory>().Create();
        foreach (var u in await db.Users.ToListAsync())
        {
            Assert.DoesNotContain("@2026", u.PasswordHash);
            Assert.True(u.PasswordHash.Length > 40);
        }
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account_for_fifteen_minutes()
    {
        await using var t = await TestDb.CreateAsync();
        var auth = t.Get<AuthService>();
        for (var i = 0; i < AuthService.MaxFailedAttempts - 1; i++)
            Assert.Equal(LoginResult.InvalidCredentials, (await auth.LoginAsync("manager", "wrong")).Result);
        var (last, until) = await auth.LoginAsync("manager", "wrong");
        Assert.Equal(LoginResult.LockedOut, last);
        Assert.Equal(LoginResult.LockedOut, (await auth.LoginAsync("manager", "Manager@2026")).Result); // correct password still refused
        t.Clock.Set(until!.Value.AddMinutes(1));
        Assert.Equal(LoginResult.Success, (await auth.LoginAsync("manager", "Manager@2026")).Result);

        var audit = await t.Get<AuditService>().ListAsync(new PageRequest(PageSize: 100), action: AuditAction.LoginFailed);
        Assert.True(audit.TotalCount >= AuditService_Min);
    }

    private const int AuditService_Min = 5;

    [Fact]
    public async Task Roles_are_enforced_by_the_services_not_only_the_ui()
    {
        await using var t = await TestDb.CreateAsync();
        await t.LoginAsync("sales", "Sales@2026");
        var perms = t.Get<PermissionService>();
        Assert.True(perms.Has(AppModule.Quotations, Permission.Create));
        Assert.False(perms.Has(AppModule.Accounting, Permission.Post));
        Assert.False(perms.Has(AppModule.Users, Permission.View));
        var acc = await t.Get<AccountingService>().PostableAccountsAsync();
        var ex = await Assert.ThrowsAsync<DomainException>(() => t.Get<AccountingService>().SaveManualDraftAsync(0, t.Clock.Now.Date, "x",
            new[] { new ManualJournalLine(acc[0].Id, 1, 0, null), new ManualJournalLine(acc[1].Id, 0, 1, null) }));
        Assert.Equal("Err.AccessDenied", ex.Code);
        await Assert.ThrowsAsync<DomainException>(() => t.Get<AuthService>().CreateUserAsync("intruder", "X", UserRole.Administrator, "Intruder2026"));

        await t.LoginAsync("store", "Store@2026");
        Assert.True(t.Get<PermissionService>().Has(AppModule.Inventory, Permission.Create));
        Assert.False(t.Get<PermissionService>().Has(AppModule.Sales, Permission.Create));
    }

    [Fact]
    public async Task Changes_are_written_to_the_audit_trail_with_user_and_fields()
    {
        await using var t = await TestDb.CreateAsync();
        var id = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Audit Co", Phone = "1" });
        var c = (await t.Get<CustomerService>().GetAsync(id))!;
        c.Phone = "2";
        await t.Get<CustomerService>().SaveAsync(c);
        var rows = (await t.Get<AuditService>().ListAsync(new PageRequest(PageSize: 50), entity: nameof(Customer))).Items.Where(r => r.RecordId == id).ToList();
        Assert.Contains(rows, r => r.Action == AuditAction.Created && r.Username == "admin");
        Assert.Contains(rows, r => r.Action == AuditAction.Updated && r.Details!.Contains("Phone"));
    }
}

public class BackupRestoreTests
{
    [Fact]
    public async Task Backup_validate_restore_round_trip_with_safety_backup()
    {
        await using var t = await TestDb.CreateAsync();
        var backup = t.Get<BackupService>();
        var customers = t.Get<CustomerService>();
        await customers.SaveAsync(new Customer { Name = "Before backup" });
        var info = await backup.CreateBackupAsync(note: "test");
        Assert.True(File.Exists(info.FilePath));
        var v = await backup.ValidateAsync(info.FilePath);
        Assert.True(v.IsValid, string.Join("; ", v.Problems));
        Assert.Equal(64, v.Manifest!.DatabaseSha256.Length);

        await customers.SaveAsync(new Customer { Name = "After backup" });
        Assert.Equal(2, (await customers.LookupAsync()).Count);

        await backup.RestoreAsync(info.FilePath);
        var names = (await customers.LookupAsync()).Select(c => c.Name).ToList();
        Assert.Contains("Before backup", names);
        Assert.DoesNotContain("After backup", names);
        var history = await backup.HistoryAsync();
        Assert.Contains(history, h => h.Kind == "PreRestore");
        Assert.Empty(backup.CheckLiveDatabase());
        await Ledger.AssertBooksBalanceAsync(t);
    }

    [Fact]
    public async Task Demo_backup_restores_into_a_fresh_installation()
    {
        string demoBackup;
        await using (var demo = await TestDb.CreateAsync(demo: true, now: DateTime.Today.AddHours(9)))
        {
            demoBackup = Path.Combine(TestDb.NewFolder("demo-backup"), "LaserWorksDemo" + BackupService.Extension);
            await demo.Get<BackupService>().CreateBackupAsync(demoBackup, "demo", "Demo");
        }
        await using var fresh = await TestDb.CreateAsync(setup: false);
        Assert.False(await fresh.Get<SetupService>().IsSetupCompletedAsync());
        await fresh.Get<BackupService>().RestoreAsync(demoBackup);
        Assert.True(await fresh.Get<SetupService>().IsSetupCompletedAsync());
        await fresh.LoginAsync("admin", "Admin@2026");
        Assert.True((await fresh.Get<CustomerService>().LookupAsync()).Count >= 8);
        await Ledger.AssertBooksBalanceAsync(fresh);
    }

    [Fact]
    public async Task Corrupted_or_foreign_files_are_rejected_before_anything_is_replaced()
    {
        await using var t = await TestDb.CreateAsync();
        var backup = t.Get<BackupService>();
        var info = await backup.CreateBackupAsync();
        var bytes = await File.ReadAllBytesAsync(info.FilePath);
        var corrupted = Path.Combine(t.Folder, "corrupted" + BackupService.Extension);
        bytes[bytes.Length / 2] ^= 0xFF;
        bytes[bytes.Length / 2 + 1] ^= 0xFF;
        await File.WriteAllBytesAsync(corrupted, bytes);
        Assert.False((await backup.ValidateAsync(corrupted)).IsValid);

        var foreign = Path.Combine(t.Folder, "notes.lwbak");
        await File.WriteAllTextAsync(foreign, "not a backup");
        Assert.False((await backup.ValidateAsync(foreign)).IsValid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backup.RestoreAsync(foreign));
        Assert.Empty(backup.CheckLiveDatabase());
    }
}
