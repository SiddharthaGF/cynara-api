using Cynara.Application.Modules.Audit.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Audit;

public static class AuditPersistenceModule
{
    public static IServiceCollection AddAuditPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IAuditRepository, AuditRepository>();
        return services;
    }
}
