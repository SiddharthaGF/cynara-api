using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Capabilities;

/// <summary>
/// EF Core implementation of the capability assignment repository. Every
/// query is scoped by hospital; the tracked reads are used by the grant and
/// revoke workflows so EF can apply the row-version concurrency token.
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
            .Where(item => item.HospitalId == hospitalId
                && item.ActorId == actorId)
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
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<CapabilityAssignment> query = track
            ? dbContext.CapabilityAssignments
            : dbContext.CapabilityAssignments.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.HospitalId == hospitalId
                && item.ActorId == actorId
                && item.Capability == capability,
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
