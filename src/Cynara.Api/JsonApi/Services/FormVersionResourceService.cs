using Cynara.Application.Forms;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Forms;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Routes form-version PATCH through draft update rules in <see cref="IFormService"/>.
/// </summary>
public sealed class FormVersionResourceService(
    IFormService formService,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<FormVersion> resourceChangeTracker)
    : TenantScopedResourceService<FormVersion, Guid>(
        deps,
        resourceChangeTracker)
{
    public override Task<FormVersion?> CreateAsync(
        FormVersion resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Create draft versions via POST /api/formDefinitions/{id}/create-draft "
            + "or by creating a form definition.");
    }

    public override async Task<FormVersion> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.FormVersions
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwned(ownership, id, "Form version");

        return await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<IReadOnlyCollection<FormVersion>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        return await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<FormVersion?> UpdateAsync(
        Guid id,
        FormVersion resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogWrite,
            cancellationToken).ConfigureAwait(false);

        FormVersion existing = await DbContext.FormVersions
            .Include(item => item.FormDefinition)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Form version '{id}' was not found.");

        if (existing.HospitalId != HospitalId
            || existing.FormDefinition.HospitalId != HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form version '{id}' was not found.");
        }

        if (existing.Status != FormVersionStatus.Draft)
        {
            throw new Application.InvalidStateException(
                "Only draft form versions can be patched via JSON:API.");
        }

        FormVersionDto updated = await formService.UpdateDraftAsync(
            existing.FormDefinition.Code,
            new UpdateFormDraftRequest(
                resource.ClinicalSchemaJson,
                resource.UiSchemaJson,
                resource.RulesSchemaJson,
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
            "Form versions cannot be hard-deleted. Soft-delete the definition "
            + "or retire published versions.");
    }
}
