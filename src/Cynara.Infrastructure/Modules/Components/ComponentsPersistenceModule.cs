using Cynara.Application.Modules.Components.Persistence;

namespace Cynara.Infrastructure.Modules.Components;

public static class ComponentsPersistenceModule
{
    public static IServiceCollection AddComponentsPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IComponentRepository, ComponentRepository>();
        return services;
    }
}
