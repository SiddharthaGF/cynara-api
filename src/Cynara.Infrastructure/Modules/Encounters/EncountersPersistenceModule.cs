using Cynara.Application.Modules.Encounters.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Encounters;

/// <summary>
/// Composition extensions that wire the encounter persistence services.
/// </summary>
public static class EncountersPersistenceModule
{
    public static IServiceCollection AddEncountersPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IEncounterRepository, EncounterRepository>();
        return services;
    }
}
