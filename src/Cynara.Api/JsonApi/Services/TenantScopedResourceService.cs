using Cynara.Api.Common.ActorContext;
using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Services;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Shared cross-cutting dependencies for tenant-scoped JSON:API resource
/// services. Bundles the JsonApiDotNetCore plumbing plus the hospital,
/// capability, audit, and persistence collaborators every service needs.
/// </summary>
public sealed record JsonApiResourceDeps(
    IResourceRepositoryAccessor RepositoryAccessor,
    IQueryLayerComposer QueryLayerComposer,
    IPaginationContext PaginationContext,
    IJsonApiOptions Options,
    ILoggerFactory LoggerFactory,
    IJsonApiRequest Request,
    IResourceDefinitionAccessor ResourceDefinitionAccessor,
    IHospitalContext HospitalContext,
    ICapabilityGuard CapabilityGuard,
    ISensitiveReadAuditor SensitiveReadAuditor,
    IHttpContextAccessor Http,
    CynaraDbContext DbContext);

/// <summary>
/// Projected ownership row used to enforce tenant scope before delegating
/// to the JsonApiDotNetCore repository layer. <see cref="DeletedAt"/> is
/// projected only by soft-delete-aware reads; a <see langword="null"/>
/// projection means the row was not found in that shape.
/// </summary>
public sealed record TenantOwnership(Guid HospitalId, DateTimeOffset? DeletedAt = null);

/// <summary>
/// Base class for resource services that enforce hospital-tenant scope and
/// capability authorization on every operation. Centralizes the guard
/// preamble, the raise-404 ownership check for cross-tenant identifiers,
/// and sensitive-read audit emission so new services cannot forget them.
/// </summary>
public abstract class TenantScopedResourceService<TResource, TId>(
    JsonApiResourceDeps deps,
    IResourceChangeTracker<TResource> resourceChangeTracker)
    : JsonApiResourceService<TResource, TId>(
        deps.RepositoryAccessor,
        deps.QueryLayerComposer,
        deps.PaginationContext,
        deps.Options,
        deps.LoggerFactory,
        deps.Request,
        resourceChangeTracker,
        deps.ResourceDefinitionAccessor)
    where TResource : class, IIdentifiable<TId>
{
    private readonly JsonApiResourceDeps deps = deps;

    protected CynaraDbContext DbContext => deps.DbContext;

    protected IHospitalContext HospitalContext => deps.HospitalContext;

    protected Guid HospitalId => deps.HospitalContext.HospitalId;

    protected HttpContext? HttpContext => deps.Http.HttpContext;

    protected string? ActorId => deps.Http.HttpContext?.GetActorId();

    /// <summary>
    /// Resolves the hospital context and requires the given capability.
    /// Every read and write must call this before touching data.
    /// </summary>
    protected async Task RequireCapabilityAsync(
        string capabilityCode,
        CancellationToken cancellationToken)
    {
        deps.HospitalContext.RequireResolved();
        await deps.CapabilityGuard
            .RequireAsync(capabilityCode, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Raises 404 when the projected row is missing or belongs to another
    /// hospital, preventing cross-tenant identifier probing.
    /// </summary>
    protected void EnsureTenantOwned(
        TenantOwnership? ownership,
        Guid id,
        string displayName)
    {
        if (ownership is null || ownership.HospitalId != deps.HospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"{displayName} '{id}' was not found.");
        }
    }

    /// <summary>
    /// Same as <see cref="EnsureTenantOwned"/> but also raises 404 for
    /// soft-deleted rows. Use only with projections that include
    /// <see cref="TenantOwnership.DeletedAt"/>.
    /// </summary>
    protected void EnsureTenantOwnedActive(
        TenantOwnership? ownership,
        Guid id,
        string displayName)
    {
        if (ownership is null
            || ownership.HospitalId != deps.HospitalContext.HospitalId
            || ownership.DeletedAt is not null)
        {
            throw new Application.NotFoundException(
                $"{displayName} '{id}' was not found.");
        }
    }

    /// <summary>
    /// Records a sensitive-read audit event for plain GET requests only.
    /// </summary>
    protected async Task RecordReadAuditAsync(
        Guid resourceId,
        string auditEntityType,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (deps.Http.HttpContext is not { } httpContext
            || !HttpMethods.IsGet(httpContext.Request.Method))
        {
            return;
        }

        await deps.SensitiveReadAuditor.RecordAsync(
            auditEntityType,
            resourceId,
            eventType,
            httpContext.GetActorId(),
            httpContext.Request.Path,
            cancellationToken).ConfigureAwait(false);
    }
}
