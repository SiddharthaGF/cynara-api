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

    /// <summary>Read-only snapshot for assertions on staged assignments.</summary>
    public IReadOnlyList<CapabilityAssignment> Assignments => assignments;

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
                .Where(item => string.Equals(
                    item.ActorId,
                    actorId,
                    StringComparison.Ordinal)
                    && ((item.HospitalId == hospitalId
                            && item.Scope.Equals(
                                CapabilityScopes.Hospital,
                                StringComparison.Ordinal))
                        || item.Scope.Equals(
                            CapabilityScopes.Platform,
                            StringComparison.Ordinal)))
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
        string scope,
        bool track,
        CancellationToken cancellationToken)
    {
        CapabilityAssignment? match = assignments.FirstOrDefault(item =>
            string.Equals(item.ActorId, actorId, StringComparison.Ordinal)
            && string.Equals(
                item.Capability,
                capability,
                StringComparison.Ordinal)
            && (scope.Equals(
                    CapabilityScopes.Platform,
                    StringComparison.Ordinal)
                ? item.Scope.Equals(
                    CapabilityScopes.Platform,
                    StringComparison.Ordinal)
                : item.HospitalId == hospitalId
                    && item.Scope.Equals(
                        CapabilityScopes.Hospital,
                        StringComparison.Ordinal)));
        return Task.FromResult(match);
    }

    public Task<bool> HasPlatformScopeAsync(
        string actorId,
        string capability,
        CancellationToken cancellationToken)
    {
        bool holds = assignments.Exists(item =>
            string.Equals(item.ActorId, actorId, StringComparison.Ordinal)
            && string.Equals(
                item.Capability,
                capability,
                StringComparison.Ordinal)
            && item.Scope.Equals(
                CapabilityScopes.Platform,
                StringComparison.Ordinal));
        return Task.FromResult(holds);
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
