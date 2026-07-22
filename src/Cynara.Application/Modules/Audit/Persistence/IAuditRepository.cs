using Cynara.Domain.Audit;

namespace Cynara.Application.Modules.Audit.Persistence;

public interface IAuditRepository
{
    public void Add(AuditEvent auditEvent);

    public Task<IReadOnlyList<AuditEvent>> ListAsync(
        string? resourceType,
        Guid? resourceId,
        string? actorId,
        int limit,
        CancellationToken cancellationToken);
}
