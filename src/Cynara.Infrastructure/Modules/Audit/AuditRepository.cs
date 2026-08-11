using Cynara.Application.Modules.Audit.Persistence;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

namespace Cynara.Infrastructure.Modules.Audit;

public sealed class AuditRepository(CynaraDbContext dbContext) : IAuditRepository
{
    public void Add(AuditEvent auditEvent)
    {
        _ = dbContext.AuditEvents.Add(auditEvent);
    }
}
