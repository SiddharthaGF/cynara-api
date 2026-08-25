using Cynara.Application.Components;
using Cynara.Application.Modules.Components;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Components;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Routes component-version writes through lifecycle services.
/// </summary>
public sealed class ComponentVersionResourceService(
    IComponentLifecycleService lifecycle,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<ComponentVersion> resourceChangeTracker)
    : TenantScopedResourceService<ComponentVersion, Guid>(
        deps,
        resourceChangeTracker)
{
    public override Task<ComponentVersion?> CreateAsync(
        ComponentVersion resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Create component drafts via POST "
            + "/api/componentDefinitions/{id}/create-draft.");
    }

    public override async Task<ComponentVersion> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.ComponentVersions
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwned(ownership, id, "Component version");

        return await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<IReadOnlyCollection<ComponentVersion>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        return await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<ComponentVersion?> UpdateAsync(
        Guid id,
        ComponentVersion resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogWrite,
            cancellationToken).ConfigureAwait(false);

        ComponentVersion existing = await DbContext.ComponentVersions
            .Include(item => item.ComponentDefinition)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Component version '{id}' was not found.");

        if (existing.HospitalId != HospitalId
            || existing.ComponentDefinition.HospitalId != HospitalId)
        {
            throw new Application.NotFoundException(
                $"Component version '{id}' was not found.");
        }

        if (existing.Status != ComponentVersionStatus.Draft)
        {
            throw new Application.InvalidStateException(
                "Only draft component versions can be patched.");
        }

        ComponentVersionDto updated = await lifecycle.UpdateDraftAsync(
            existing.ComponentDefinition.Code,
            new UpdateComponentDraftRequest(
                resource.ClinicalSchemaJson,
                resource.UiSchemaJson,
                resource.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(updated.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Component versions cannot be hard-deleted via JSON:API.");
    }
}
