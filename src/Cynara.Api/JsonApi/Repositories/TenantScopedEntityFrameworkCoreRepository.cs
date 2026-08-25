using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Common;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;

namespace Cynara.Api.JsonApi.Repositories;

/// <summary>
/// Read repository for hospital-scoped resources that pushes the tenant
/// predicate into SQL as the first <c>WHERE</c> clause, before
/// JsonApiDotNetCore applies request filters, sorting, pagination, and
/// includes. Collection pages, page slices, and <c>meta.total</c> counts
/// therefore reflect only the caller's hospital instead of a global slice.
///
/// The predicate is applied only while serving a top-level primary
/// collection request (for example <c>GET /api/formDefinitions</c>). Every
/// other path — by-id reads, secondary and relationship endpoints, and all
/// write flows — keeps the framework's unscoped starting queryable because
/// resource services already enforce ownership there with explicit checks,
/// preserving their exact error behavior.
/// </summary>
public sealed class TenantScopedEntityFrameworkCoreRepository<TResource, TId>(
    ITargetedFields targetedFields,
    IDbContextResolver dbContextResolver,
    IResourceGraph resourceGraph,
    IResourceFactory resourceFactory,
    IEnumerable<IQueryConstraintProvider> constraintProviders,
    ILoggerFactory loggerFactory,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IHospitalContext hospitalContext,
    IJsonApiRequest request)
    : EntityFrameworkCoreRepository<TResource, TId>(
        targetedFields,
        dbContextResolver,
        resourceGraph,
        resourceFactory,
        constraintProviders,
        loggerFactory,
        resourceDefinitionAccessor)
    where TResource : class, IIdentifiable<TId>, IHospitalScopedResource
{
    protected override IQueryable<TResource> GetAll()
    {
        IQueryable<TResource> queryable = base.GetAll();

        if (!IsTenantScopedCollectionRead())
        {
            return queryable;
        }

        Guid hospitalId = hospitalContext.HospitalId;
        return queryable.Where(item => item.HospitalId == hospitalId);
    }

    private bool IsTenantScopedCollectionRead()
    {
        return request.Kind == EndpointKind.Primary
            && request.PrimaryId is null
            && request.IsCollection;
    }
}
