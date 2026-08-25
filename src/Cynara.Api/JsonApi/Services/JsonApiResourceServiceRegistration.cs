using Cynara.Api.JsonApi.Repositories;
using Cynara.Domain.Audit;
using Cynara.Domain.Components;
using Cynara.Domain.Documents;
using Cynara.Domain.Forms;
using Cynara.Domain.Workflows;

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

        RegisterTenantScopedRepositories(services);

        return services;
    }

    /// <summary>
    /// Replaces the default repository for every hospital-scoped resource so
    /// top-level collection reads push the tenant predicate into SQL before
    /// pagination, sorting, and filters are applied. AiProviderSettings is
    /// not hospital-scoped and keeps the shared default repository.
    /// </summary>
    private static void RegisterTenantScopedRepositories(
        IServiceCollection services)
    {
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<FormDefinition, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<FormVersion, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<FormResponse, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<FormResponseRevision, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<ComponentDefinition, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<ComponentVersion, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<AuditEvent, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<DocumentDefinition, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<WorkflowDefinition, Guid>>();
        _ = services.AddResourceRepository<
            TenantScopedEntityFrameworkCoreRepository<WorkflowVersion, Guid>>();
    }
}
