using Cynara.Application.Modules.Documents;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Documents;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Creates, updates, and reads document catalog entries through the
/// application service so draft validation, ownership checks, and audit
/// emission stay in the application layer. Resource reads enforce tenant
/// scope by raising 404 for cross-tenant identifiers, preventing one
/// hospital from probing another hospital's catalog.
/// </summary>
public sealed class DocumentDefinitionResourceService(
    IDocumentCatalogService catalog,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<DocumentDefinition> resourceChangeTracker)
    : TenantScopedResourceService<DocumentDefinition, Guid>(
        deps,
        resourceChangeTracker)
{
    public override async Task<DocumentDefinition?> CreateAsync(
        DocumentDefinition resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogWrite,
            cancellationToken).ConfigureAwait(false);

        Guid formVersionId = resource.FormVersionId != Guid.Empty
            ? resource.FormVersionId
            : resource.FormVersion?.Id
                ?? throw new Application.ValidationException(
                    "Form version is required.");
        Guid facilityId = resource.FacilityId != Guid.Empty
            ? resource.FacilityId
            : resource.Facility?.Id
                ?? throw new Application.ValidationException(
                    "Facility is required.");
        Guid clinicalAreaId = resource.ClinicalAreaId != Guid.Empty
            ? resource.ClinicalAreaId
            : resource.ClinicalArea?.Id
                ?? throw new Application.ValidationException(
                    "Clinical area is required.");
        Guid disciplineId = resource.DisciplineId != Guid.Empty
            ? resource.DisciplineId
            : resource.Discipline?.Id
                ?? throw new Application.ValidationException(
                    "Discipline is required.");

        CreateDocumentDefinitionRequest createRequest = new(
            resource.Code,
            resource.Name,
            formVersionId,
            facilityId,
            clinicalAreaId,
            disciplineId,
            resource.AllowsMultipleInstancesPerEncounter,
            resource.RequiresActorForCreation,
            resource.RequiresActorForCompletion);

        DocumentDefinitionDto created = await catalog
            .CreateAsync(createRequest, ActorId, cancellationToken)
            .ConfigureAwait(false);

        return await LoadWithRelationshipsAsync(created.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<DocumentDefinition> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.DocumentDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwned(ownership, id, "Document definition");

        return await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<IReadOnlyCollection<DocumentDefinition>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);
        bool includeRetired = HttpContext?.Request.Query
            .TryGetValue(
                "includeRetired",
                out Microsoft.Extensions.Primitives.StringValues values)
            == true
            && bool.TryParse(values.ToString(), out bool parsed)
            && parsed;
        IReadOnlyCollection<DocumentDefinition> entries = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        HospitalContext.RequireResolved();
        IEnumerable<DocumentDefinition> scoped = entries
            .Where(item => item.HospitalId == HospitalId);
        if (!includeRetired)
        {
            scoped = scoped.Where(
                item => item.Status == DocumentDefinitionStatus.Active);
        }

        return [.. scoped];
    }

    public override async Task<object?> GetSecondaryAsync(
        Guid id,
        string relationshipName,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogRead,
            cancellationToken).ConfigureAwait(false);

        if (await base.GetSecondaryAsync(
                id,
                relationshipName,
                cancellationToken)
            .ConfigureAwait(false) is not DocumentDefinition entry)
        {
            return null;
        }

        HospitalContext.RequireResolved();
        if (entry.HospitalId != HospitalId)
        {
            return null;
        }

        return entry;
    }

    public override async Task<DocumentDefinition?> UpdateAsync(
        Guid id,
        DocumentDefinition resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.CatalogWrite,
            cancellationToken).ConfigureAwait(false);

        UpdateDocumentDefinitionRequest updateRequest = new(
            resource.Name,
            resource.AllowsMultipleInstancesPerEncounter,
            resource.RequiresActorForCreation,
            resource.RequiresActorForCompletion,
            resource.RowVersion);

        DocumentDefinitionDto updated = await catalog
            .UpdateAsync(id, updateRequest, ActorId, cancellationToken)
            .ConfigureAwait(false);

        return await LoadWithRelationshipsAsync(updated.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Document catalog entries cannot be hard-deleted; "
            + "use POST /api/documentDefinitions/{id}/retire to retire them.");
    }

    private async Task<DocumentDefinition> LoadWithRelationshipsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await DbContext.DocumentDefinitions
            .AsNoTracking()
            .Include(item => item.FormDefinition)
            .Include(item => item.FormVersion)
            .Include(item => item.Facility)
            .Include(item => item.ClinicalArea)
            .Include(item => item.Discipline)
            .SingleAsync(
                item => item.Id == id && item.HospitalId == HospitalId,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
