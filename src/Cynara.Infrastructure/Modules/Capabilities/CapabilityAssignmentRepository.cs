using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Capabilities;

/// <summary>
/// EF Core implementation of the capability assignment repository.
/// Resolution is the union of the actor's hospital-scoped grants for the
/// resolved hospital and their platform-scoped grants; every tracked read
/// feeds the grant and revoke workflows so EF can apply the row-version
/// concurrency token.
/// </summary>
public sealed class CapabilityAssignmentRepository(CynaraDbContext dbContext)
    : ICapabilityAssignmentRepository
{
    public async Task<IReadOnlyList<string>> ListCapabilityCodesAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CapabilityAssignments
            .AsNoTracking()
            .Where(item => item.ActorId == actorId
                && ((item.HospitalId == hospitalId
                        && item.Scope == CapabilityScopes.Hospital)
                    || item.Scope == CapabilityScopes.Platform))
            .Select(item => item.Capability)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CapabilityAssignment>> ListAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CapabilityAssignments
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId)
            .OrderByDescending(item => item.AssignedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<CapabilityAssignment?> FindAsync(
        Guid hospitalId,
        string actorId,
        string capability,
        string scope,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<CapabilityAssignment> query = track
            ? dbContext.CapabilityAssignments
            : dbContext.CapabilityAssignments.AsNoTracking();
        query = query.Where(item => item.ActorId == actorId
            && item.Capability == capability
            && (scope == CapabilityScopes.Platform
                ? item.Scope == CapabilityScopes.Platform
                : item.HospitalId == hospitalId
                    && item.Scope == CapabilityScopes.Hospital));
        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(CapabilityAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _ = dbContext.CapabilityAssignments.Add(assignment);
    }

    public void Remove(CapabilityAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _ = dbContext.CapabilityAssignments.Remove(assignment);
    }
}
