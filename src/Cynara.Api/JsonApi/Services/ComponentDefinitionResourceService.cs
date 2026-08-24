using Cynara.Application.Components;
using Cynara.Application.Modules.Components;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Components;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Creates component definitions through application lifecycle services.
/// </summary>
public sealed class ComponentDefinitionResourceService(
    IComponentLifecycleService lifecycle,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<ComponentDefinition> resourceChangeTracker)
    : TenantScopedResourceService<ComponentDefinition, Guid>(
        deps,
        resourceChangeTracker)
{
    public override async Task<ComponentDefinition?> CreateAsync(
        ComponentDefinition resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogWrite,
            cancellationToken).ConfigureAwait(false);

        string clinical = string.IsNullOrWhiteSpace(
            resource.InitialClinicalSchemaJson)
            ? /*lang=json,strict*/ """{"schemaVersion":"1.0.0","fields":[{"id":"placeholder","code":"component.placeholder","type":"text"}]}"""
            : resource.InitialClinicalSchemaJson;

        ComponentSummaryDto created = await lifecycle.CreateAsync(
            new CreateComponentRequest(
                resource.Code,
                resource.Name,
                clinical,
                resource.InitialUiSchemaJson),
            ActorId,
            cancellationToken).ConfigureAwait(false);

        ComponentDefinition definition = await DbContext.ComponentDefinitions
            .AsNoTracking()
            .SingleAsync(
                item => item.Code == created.Code
                    && item.HospitalId == HospitalId,
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(definition.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Component definitions cannot be hard-deleted via JSON:API.");
    }

    public override async Task<ComponentDefinition> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.ComponentDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId, item.DeletedAt))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwnedActive(ownership, id, "Component definition");

        return await base.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<IReadOnlyCollection<ComponentDefinition>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<ComponentDefinition> definitions = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. definitions.Where(item => item.HospitalId == HospitalId)];
    }
}
