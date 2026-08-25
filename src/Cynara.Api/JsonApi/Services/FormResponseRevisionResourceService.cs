using Cynara.Domain.Capabilities;
using Cynara.Domain.Forms;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Tenant-scoped read-only resource service for form response revisions.
/// Filters collection and single-resource reads by the resolved hospital
/// workspace so one tenant cannot enumerate another tenant's revisions.
/// </summary>
public sealed class FormResponseRevisionResourceService(
    JsonApiResourceDeps deps,
    IResourceChangeTracker<FormResponseRevision> resourceChangeTracker)
    : TenantScopedResourceService<FormResponseRevision, Guid>(
        deps,
        resourceChangeTracker)
{
    public override Task<FormResponseRevision?> CreateAsync(
        FormResponseRevision resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "FormResponseRevision is read-only over JSON:API.");
    }

    public override Task<FormResponseRevision?> UpdateAsync(
        Guid id,
        FormResponseRevision resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "FormResponseRevision is read-only over JSON:API.");
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "FormResponseRevision is read-only over JSON:API.");
    }

    public override async Task<FormResponseRevision> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.FormResponsesRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.FormResponseRevisions
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwned(ownership, id, "Form response revision");

        return await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<IReadOnlyCollection<FormResponseRevision>>
        GetAsync(CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.FormResponsesRead,
            cancellationToken).ConfigureAwait(false);

        return await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
