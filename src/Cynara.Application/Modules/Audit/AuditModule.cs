using Cynara.Application.Audit;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IAuditService, AuditService>();
        _ = services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }
}
