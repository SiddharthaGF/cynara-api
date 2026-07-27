using Cynara.Application.Audit;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// Records audit calls so tests can assert metadata and ordering without
/// spinning up the persistence stack.
/// </summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    private readonly List<AuditEntry> entries = [];

    public IReadOnlyCollection<AuditEntry> Entries => entries;

    public void Append(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId,
        DateTimeOffset occurredAt,
        object metadata)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(metadata);
        entries.Add(
            new AuditEntry(resourceType, resourceId, action, actorId, occurredAt, metadata));
    }

    public sealed record AuditEntry(
        string ResourceType,
        Guid ResourceId,
        string Action,
        string? ActorId,
        DateTimeOffset OccurredAt,
        object Metadata);
}
