namespace Cynara.Application.Audit;

public interface IAuditService
{
    public Task<IReadOnlyList<AuditEventDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken);
}
