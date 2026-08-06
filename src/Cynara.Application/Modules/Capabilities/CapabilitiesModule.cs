using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Composition extensions that wire the capability authorization services.
/// The repository implementation is registered in
/// <c>Cynara.Infrastructure.Modules.Capabilities</c> so the application layer
/// remains persistence-agnostic. <see cref="ICurrentActor"/> and the
/// <see cref="EffectiveCapabilityResolver"/> are scoped so each request gets
/// a memoized resolution; the default actor reads the scoped
/// <see cref="CurrentActorOverride"/>, and the Api host replaces
/// <see cref="ICurrentActor"/> with its header-backed implementation.
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
