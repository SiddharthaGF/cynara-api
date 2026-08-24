using Cynara.Application.Modules.Capabilities.Persistence;

namespace Cynara.Infrastructure.Modules.Capabilities;

/// <summary>
/// Composition extensions that wire the capability persistence services. The
/// application layer remains persistence-agnostic; only the infrastructure
/// composition root registers the EF Core repository implementation.
/// </summary>
public static class CapabilitiesPersistenceModule
{
    public static IServiceCollection AddCapabilitiesPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<
            ICapabilityAssignmentRepository,
            CapabilityAssignmentRepository>();
        return services;
    }
}
