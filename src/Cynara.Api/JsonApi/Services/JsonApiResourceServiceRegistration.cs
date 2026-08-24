using JsonApiDotNetCore.Configuration;

namespace Cynara.Api.JsonApi.Services;

internal static class JsonApiResourceServiceRegistration
{
    public static IServiceCollection AddCynaraJsonApiResourceServices(
        this IServiceCollection services)
    {
        _ = services.AddScoped<JsonApiResourceDeps>();
        _ = services.AddResourceService<FormDefinitionResourceService>();
        _ = services.AddResourceService<FormVersionResourceService>();
        _ = services.AddResourceService<FormResponseResourceService>();
        _ = services.AddResourceService<ComponentDefinitionResourceService>();
        _ = services.AddResourceService<ComponentVersionResourceService>();
        _ = services.AddResourceService<FormResponseRevisionResourceService>();
        _ = services.AddResourceService<AuditEventResourceService>();
        _ = services.AddResourceService<AiProviderSettingsResourceService>();
        _ = services.AddResourceService<DocumentDefinitionResourceService>();
        _ = services.AddResourceService<WorkflowDefinitionResourceService>();
        _ = services.AddResourceService<WorkflowVersionResourceService>();

        return services;
    }
}
