using Cynara.Api.Common.ActorContext;
using Cynara.Application.Components;
using Cynara.Application.Modules.Components;
using Cynara.Domain.Components;
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
/// Routes component-version writes through lifecycle services.
/// </summary>
public sealed class ComponentVersionResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<ComponentVersion> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IComponentLifecycleService lifecycle,
    IHttpContextAccessor httpContextAccessor,
    CynaraDbContext dbContext)
    : JsonApiResourceService<ComponentVersion, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
{
    public override Task<ComponentVersion?> CreateAsync(
        ComponentVersion resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Create component drafts via POST "
            + "/api/componentDefinitions/{id}/create-draft.");
    }

    public override async Task<ComponentVersion?> UpdateAsync(
        Guid id,
        ComponentVersion resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        ComponentVersion existing = await dbContext.ComponentVersions
            .Include(item => item.ComponentDefinition)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Component version '{id}' was not found.");

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
            "Component versions cannot be hard-deleted via JSON:API.");
    }
}
