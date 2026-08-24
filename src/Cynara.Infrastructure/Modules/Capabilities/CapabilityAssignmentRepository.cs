using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Domain.Capabilities;

namespace Cynara.Infrastructure.Modules.Capabilities;

/// <summary>
/// EF Core implementation of the capability assignment repository;
/// resolution unions hospital- and platform-scoped grants, and tracked
/// reads feed workflows so row-version concurrency applies.
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

    public Task<bool> HasPlatformScopeAsync(
        string actorId,
        string capability,
        CancellationToken cancellationToken)
    {
        return dbContext.CapabilityAssignments
            .AsNoTracking()
            .AnyAsync(
                item => item.ActorId == actorId
                    && item.Capability == capability
                    && item.Scope == CapabilityScopes.Platform,
                cancellationToken);
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
