namespace Cynara.Application.Audit;

/// <summary>
/// Stages and commits a read audit event for a sensitive clinical record
/// read. Reads are non-mutating workflows, so unlike the writer-side audit
/// events that ride the mutation's unit-of-work transaction, the read
/// auditor owns its own immediate commit.
/// </summary>
public interface ISensitiveReadAuditor
{
    public Task RecordAsync(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId,
        string? requestPath,
        CancellationToken cancellationToken);
}
