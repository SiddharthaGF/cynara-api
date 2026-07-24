using Cynara.Domain.Audit;

namespace Cynara.Application.Modules.Audit.Persistence;

public interface IAuditRepository
{
    public void Add(AuditEvent auditEvent);

    public Task<IReadOnlyList<AuditEvent>> ListAsync(
        Guid hospitalId,
        string? resourceType,
        Guid? resourceId,
        string? actorId,
        int limit,
        CancellationToken cancellationToken);
}
