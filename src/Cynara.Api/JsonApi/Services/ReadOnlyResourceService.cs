using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Services;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Resource service that allows reads but rejects all write operations.
/// </summary>
/// <typeparam name="TResource">The JSON:API resource type.</typeparam>
/// <typeparam name="TId">The resource identifier type.</typeparam>
public class ReadOnlyResourceService<TResource, TId>(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<TResource> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor)
    : JsonApiResourceService<TResource, TId>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
    where TResource : class, IIdentifiable<TId>
{
    public override Task<TResource?> CreateAsync(
        TResource resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            $"{typeof(TResource).Name} is read-only over JSON:API.");
    }

    public override Task<TResource?> UpdateAsync(
        TId id,
        TResource resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            $"{typeof(TResource).Name} is read-only over JSON:API.");
    }

    public override Task DeleteAsync(TId id, CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            $"{typeof(TResource).Name} is read-only over JSON:API.");
    }
}
