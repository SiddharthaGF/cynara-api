namespace Cynara.Application.Audit;

public interface IAuditWriter
{
    public void Append(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId,
        DateTimeOffset occurredAt,
        object metadata);
}
