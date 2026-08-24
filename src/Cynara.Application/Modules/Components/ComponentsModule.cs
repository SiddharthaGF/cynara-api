namespace Cynara.Application.Modules.Components;

public static class ComponentsModule
{
    public static IServiceCollection AddComponentsModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IComponentLifecycleService, ComponentLifecycleService>();
        _ = services.AddScoped<IComponentQueryService, ComponentQueriesService>();
        return services;
    }
}
