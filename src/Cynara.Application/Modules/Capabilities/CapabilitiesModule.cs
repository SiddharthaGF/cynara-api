using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Composition extensions wiring the capability authorization services.
/// The repository registers in Infrastructure so Application stays
/// persistence-agnostic; actor and resolver registrations are scoped so
/// each request gets a memoized resolution.
/// </summary>
public static class CapabilitiesModule
{
    public static IServiceCollection AddCapabilitiesModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<CurrentActorOverride>();
        _ = services.AddScoped<ICurrentActor, DefaultCurrentActor>();
        _ = services.AddScoped<EffectiveCapabilityResolver>();
        _ = services.AddScoped<IEffectiveCapabilityResolver>(
            provider => provider.GetRequiredService<EffectiveCapabilityResolver>());
        _ = services.AddScoped<ICapabilityGuard, CapabilityGuard>();
        _ = services.AddScoped<ICapabilityAssignmentService, CapabilityAssignmentService>();
        _ = services.AddScoped<IDeniedAccessAuditor, DeniedAccessAuditor>();
        return services;
    }
}
