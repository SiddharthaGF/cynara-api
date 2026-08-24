namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Composition extensions that wire the encounter application services.
/// The repository is registered in
/// <c>Cynara.Infrastructure.Modules.Encounters</c> so the application layer
/// remains persistence-agnostic.
/// </summary>
public static class EncountersModule
{
    public static IServiceCollection AddEncountersModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IEncounterService, EncounterService>();
        return services;
    }
}
