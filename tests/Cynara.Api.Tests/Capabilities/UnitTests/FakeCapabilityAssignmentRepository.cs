using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Domain.Capabilities;

namespace Cynara.Api.Tests.Capabilities.UnitTests;

/// <summary>
/// In-memory capability assignment repository for unit tests. Records how
/// often the code-list lookup is hit so tests can assert resolver
/// memoization.
/// </summary>
internal sealed class FakeCapabilityAssignmentRepository
    : ICapabilityAssignmentRepository
{
    private readonly List<CapabilityAssignment> assignments = [];

    public int ListCapabilityCodesCalls { get; private set; }

    public void Seed(CapabilityAssignment assignment)
    {
        assignments.Add(assignment);
    }

    public Task<IReadOnlyList<string>> ListCapabilityCodesAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken)
    {
        ListCapabilityCodesCalls++;
        IReadOnlyList<string> codes = [
            .. assignments
                .Where(item => item.HospitalId == hospitalId
                    && string.Equals(
                        item.ActorId,
                        actorId,
                        StringComparison.Ordinal))
                .Select(item => item.Capability),
        ];
        return Task.FromResult(codes);
    }

    public Task<IReadOnlyList<CapabilityAssignment>> ListAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CapabilityAssignment> items = [
            .. assignments
                .Where(item => item.HospitalId == hospitalId)
                .OrderByDescending(item => item.AssignedAt),
        ];
        return Task.FromResult(items);
    }

    public Task<CapabilityAssignment?> FindAsync(
        Guid hospitalId,
        string actorId,
        string capability,
        bool track,
        CancellationToken cancellationToken)
    {
        CapabilityAssignment? match = assignments.FirstOrDefault(item =>
            item.HospitalId == hospitalId
            && string.Equals(
                item.ActorId,
                actorId,
                StringComparison.Ordinal)
            && string.Equals(
                item.Capability,
                capability,
                StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public void Add(CapabilityAssignment assignment)
    {
        assignments.Add(assignment);
    }

    public void Remove(CapabilityAssignment assignment)
    {
        _ = assignments.Remove(assignment);
    }
}
