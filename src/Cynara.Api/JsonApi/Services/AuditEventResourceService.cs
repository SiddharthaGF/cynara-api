using Cynara.Domain.Audit;
using Cynara.Domain.Capabilities;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Tenant-scoped read-only resource service for audit events.
/// Filters collection and single-resource reads by the resolved hospital
/// workspace so one tenant cannot enumerate another tenant's audit trail.
/// </summary>
public sealed class AuditEventResourceService(
    JsonApiResourceDeps deps,
    IResourceChangeTracker<AuditEvent> resourceChangeTracker)
    : TenantScopedResourceService<AuditEvent, Guid>(deps, resourceChangeTracker)
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
        await RequireCapabilityAsync(
            CapabilityCodes.AuditRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwned(ownership, id, "Audit event");

        return await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<IReadOnlyCollection<AuditEvent>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.AuditRead,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<AuditEvent> events = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. events.Where(item => item.HospitalId == HospitalId)];
    }
}
