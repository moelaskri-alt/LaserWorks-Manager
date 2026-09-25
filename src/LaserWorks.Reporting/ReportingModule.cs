using LaserWorks.Reporting.Core;
using Microsoft.Extensions.DependencyInjection;

namespace LaserWorks.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddLaserWorksReporting(this IServiceCollection services)
    {
        services.AddSingleton<ReportCatalog>();
        return services;
    }
}
