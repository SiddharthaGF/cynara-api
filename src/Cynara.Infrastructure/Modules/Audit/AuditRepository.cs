using Cynara.Application.Modules.Audit.Persistence;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Audit;

public sealed class AuditRepository(CynaraDbContext dbContext) : IAuditRepository
{
    public void Add(AuditEvent auditEvent)
    {
        _ = dbContext.AuditEvents.Add(auditEvent);
    }

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(
        string? resourceType,
        Guid? resourceId,
        string? actorId,
        int limit,
        CancellationToken cancellationToken)
    {
        IQueryable<AuditEvent> query = dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            query = query.Where(item => item.ResourceType == resourceType);
        }

        if (resourceId is not null)
        {
            query = query.Where(item => item.ResourceId == resourceId);
        }

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            query = query.Where(item => item.ActorId == actorId);
        }

        List<AuditEvent> items = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. items
            .OrderByDescending(item => item.OccurredAt)
            .Take(limit)];
    }
}
