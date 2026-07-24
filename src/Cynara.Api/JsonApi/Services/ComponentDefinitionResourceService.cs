using Cynara.Api.Common.ActorContext;
using Cynara.Application.Components;
using Cynara.Application.Modules.Components;
using Cynara.Application.Modules.Hospitals;
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
/// Creates component definitions through application lifecycle services.
/// </summary>
public sealed class ComponentDefinitionResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<ComponentDefinition> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IComponentLifecycleService lifecycle,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor,
    CynaraDbContext dbContext)
    : JsonApiResourceService<ComponentDefinition, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
{
    public override async Task<ComponentDefinition?> CreateAsync(
        ComponentDefinition resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        hospitalContext.RequireResolved();

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
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);

        ComponentDefinition definition = await dbContext.ComponentDefinitions
            .AsNoTracking()
            .SingleAsync(
                item => item.Code == created.Code
                    && item.HospitalId == hospitalContext.HospitalId,
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
        hospitalContext.RequireResolved();

        var ownership = await dbContext.ComponentDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId, item.DeletedAt })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null
            || ownership.HospitalId != hospitalContext.HospitalId
            || ownership.DeletedAt is not null)
        {
            throw new Application.NotFoundException(
                $"Component definition '{id}' was not found.");
        }

        return await base.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }
}
