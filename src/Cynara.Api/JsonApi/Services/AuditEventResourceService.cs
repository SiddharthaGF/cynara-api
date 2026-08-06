using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Audit;
using Cynara.Domain.Capabilities;
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
/// Tenant-scoped read-only resource service for audit events.
/// Filters collection and single-resource reads by the resolved hospital
/// workspace so one tenant cannot enumerate another tenant's audit trail.
/// </summary>
public sealed class AuditEventResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<AuditEvent> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard,
    CynaraDbContext dbContext)
    : JsonApiResourceService<AuditEvent, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
{
    public override Task<AuditEvent?> CreateAsync(
        AuditEvent resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "AuditEvent is read-only over JSON:API.");
    }

    public override Task<AuditEvent?> UpdateAsync(
        Guid id,
        AuditEvent resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "AuditEvent is read-only over JSON:API.");
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "AuditEvent is read-only over JSON:API.");
    }

    public override async Task<AuditEvent> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.AuditRead, cancellationToken)
            .ConfigureAwait(false);

        var ownership = await dbContext.AuditEvents
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null
            || ownership.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Audit event '{id}' was not found.");
        }

        AuditEvent? auditEvent = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return auditEvent!;
    }

    public override async Task<IReadOnlyCollection<AuditEvent>> GetAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.AuditRead, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyCollection<AuditEvent> events = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. events.Where(item => item.HospitalId == hospitalContext.HospitalId)];
    }
}
