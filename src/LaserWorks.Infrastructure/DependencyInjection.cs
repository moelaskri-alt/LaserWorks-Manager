using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Services;
using LaserWorks.Infrastructure.Backup;
using LaserWorks.Infrastructure.Files;
using LaserWorks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LaserWorks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence, file storage and all application services.</summary>
    public static IServiceCollection AddLaserWorks(this IServiceCollection services, IAppPaths paths, MutableClock? clock = null)
    {
        clock ??= new MutableClock();
        services.AddSingleton(paths);
        services.AddSingleton(clock);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<UserSession>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<UserSession>());
        services.AddSingleton(sp => new SqliteDbFactory(paths.DatabasePath, sp.GetRequiredService<ICurrentUser>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton<IAppDbFactory>(sp => sp.GetRequiredService<SqliteDbFactory>());
        services.AddSingleton<IAttachmentStore, FileAttachmentStore>();
        services.AddSingleton<BackupService>();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<PermissionService>();
        services.AddSingleton<ServiceContext>();
        services.AddSingleton<SeedService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<CustomerService>();
        services.AddSingleton<SupplierService>();
        services.AddSingleton<EmployeeService>();
        services.AddSingleton<MachineService>();
        services.AddSingleton<MaterialService>();
        services.AddSingleton<LookupService>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<RequestService>();
        services.AddSingleton<DesignService>();
        services.AddSingleton<EstimateService>();
        services.AddSingleton<QuotationService>();
        services.AddSingleton<JobService>();
        services.AddSingleton<JobComponentService>();
        services.AddSingleton<ProductTemplateService>();
        services.AddSingleton<ProductionService>();
        services.AddSingleton<JobCostingService>();
        services.AddSingleton<SalesService>();
        services.AddSingleton<PurchaseService>();
        services.AddSingleton<ExpenseService>();
        services.AddSingleton<AccountingService>();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<ReconciliationService>();
        services.AddSingleton<DemoDataService>();
        services.AddSingleton<SetupService>();
        return services;
    }

    /// <summary>Creates/migrates the database and seeds base data.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider sp)
    {
        await DatabaseInitializer.MigrateAsync(sp.GetRequiredService<SqliteDbFactory>());
        await sp.GetRequiredService<SeedService>().EnsureBaseDataAsync();
        sp.GetRequiredService<SettingsService>().Invalidate();
        sp.GetRequiredService<PermissionService>().Invalidate();
    }
}
