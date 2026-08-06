using Cynara.Api.Common.ActorContext;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Services;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Routes form-version PATCH through draft update rules in <see cref="IFormService"/>.
/// </summary>
public sealed class FormVersionResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<FormVersion> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IFormService formService,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor,
    ICapabilityGuard capabilityGuard,
    CynaraDbContext dbContext)
    : JsonApiResourceService<FormVersion, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
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
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);

        var ownership = await dbContext.FormVersions
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null || ownership.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form version '{id}' was not found.");
        }

        FormVersion? version = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return version!;
    }

    public override async Task<IReadOnlyCollection<FormVersion>> GetAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyCollection<FormVersion> versions = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. versions.Where(
            item => item.HospitalId == hospitalContext.HospitalId)];
    }

    public override async Task<FormVersion?> UpdateAsync(
        Guid id,
        FormVersion resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        FormVersion existing = await dbContext.FormVersions
            .Include(item => item.FormDefinition)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Form version '{id}' was not found.");

        if (existing.HospitalId != hospitalContext.HospitalId
            || existing.FormDefinition.HospitalId != hospitalContext.HospitalId)
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
            httpContextAccessor.HttpContext?.GetActorId(),
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
