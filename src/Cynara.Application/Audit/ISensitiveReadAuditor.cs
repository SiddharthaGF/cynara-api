namespace Cynara.Application.Audit;

/// <summary>
/// Stages and commits a read audit event for a sensitive clinical record
/// read. Unlike writer-side audit events riding the mutation's unit-of-work
/// transaction, reads are non-mutating so this auditor owns its own
/// immediate commit.
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
