using Cynara.Domain.Audit;

namespace Cynara.Application.Modules.Audit.Persistence;

public interface IAuditRepository
{
    public void Add(AuditEvent auditEvent);
}
