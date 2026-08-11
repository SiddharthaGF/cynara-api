using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.IdentitySpike.Data;

using Microsoft.EntityFrameworkCore;

namespace Cynara.IdentitySpike.Auth;

/// <summary>
/// Spike implementation of the Cynara capability persistence port. Every
/// query filters by <see cref="CapabilityAssignment.HospitalId"/> so an
/// assignment in one tenant can never resolve for another, mirroring the
/// production repository.
/// </summary>
public sealed class SpikeCapabilityAssignmentRepository(
    SpikeDbContext dbContext) : ICapabilityAssignmentRepository
{
    /// <summary>Returns the capability codes granted to the actor in the hospital.</summary>
    public async Task<IReadOnlyList<string>> ListCapabilityCodesAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        List<string> codes = await dbContext.CapabilityAssignments
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId
                && item.ActorId == actorId)
            .Select(item => item.Capability)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return codes;
    }

    /// <summary>Returns all assignments for the hospital, newest first.</summary>
    public async Task<IReadOnlyList<CapabilityAssignment>> ListAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        List<CapabilityAssignment> assignments =
            await dbContext.CapabilityAssignments
                .AsNoTracking()
                .Where(item => item.HospitalId == hospitalId)
                .OrderByDescending(item => item.AssignedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        return assignments;
    }

    /// <summary>Finds the unique assignment for the triple, if any.</summary>
    public Task<CapabilityAssignment?> FindAsync(
        Guid hospitalId,
        string actorId,
        string capability,
        bool track,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(capability);
        IQueryable<CapabilityAssignment> query = track
            ? dbContext.CapabilityAssignments
            : dbContext.CapabilityAssignments.AsNoTracking();
        return query.FirstOrDefaultAsync(
            item => item.HospitalId == hospitalId
                && item.ActorId == actorId
                && item.Capability == capability,
            cancellationToken);
    }

    /// <summary>Stages a new assignment for the next save.</summary>
    public void Add(CapabilityAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _ = dbContext.CapabilityAssignments.Add(assignment);
    }

    /// <summary>Stages an assignment removal for the next save.</summary>
    public void Remove(CapabilityAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _ = dbContext.CapabilityAssignments.Remove(assignment);
    }
}
