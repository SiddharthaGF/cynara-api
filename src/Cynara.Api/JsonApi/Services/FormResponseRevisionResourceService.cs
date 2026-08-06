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
/// Tenant-scoped read-only resource service for form response revisions.
/// Filters collection and single-resource reads by the resolved hospital
/// workspace so one tenant cannot enumerate another tenant's revisions.
/// </summary>
public sealed class FormResponseRevisionResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<FormResponseRevision> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard,
    CynaraDbContext dbContext)
    : JsonApiResourceService<FormResponseRevision, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
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
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesRead, cancellationToken)
            .ConfigureAwait(false);

        var ownership = await dbContext.FormResponseRevisions
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null
            || ownership.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form response revision '{id}' was not found.");
        }

        FormResponseRevision? revision = await base.GetAsync(
            id, cancellationToken).ConfigureAwait(false);
        return revision!;
    }

    public override async Task<IReadOnlyCollection<FormResponseRevision>>
        GetAsync(CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesRead, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyCollection<FormResponseRevision> revisions = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. revisions.Where(item => item.HospitalId == hospitalContext.HospitalId)];
    }
}
