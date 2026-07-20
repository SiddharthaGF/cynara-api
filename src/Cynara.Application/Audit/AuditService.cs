using Cynara.Application;
using Cynara.Application.Persistence;
using Cynara.Domain.Audit;

namespace Cynara.Application.Audit;

public sealed class AuditService(IAuditRepository audit) : IAuditService
{
    private const int MaxLimit = 200;

    public async Task<IReadOnlyList<AuditEventDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        if (query.ResourceId is null
            && string.IsNullOrWhiteSpace(query.ResourceType)
            && string.IsNullOrWhiteSpace(query.ActorId))
        {
            throw new ValidationException("At least one audit filter is required.");
        }

        int limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, MaxLimit);
        IReadOnlyList<AuditEvent> events = await audit.ListAsync(
            query.ResourceType,
            query.ResourceId,
            query.ActorId,
            limit,
            cancellationToken);

        return [.. events.Select(ToDto)];
    }

    private static AuditEventDto ToDto(AuditEvent auditEvent)
    {
        return new AuditEventDto(
            auditEvent.Id,
            auditEvent.ResourceType,
            auditEvent.ResourceId,
            auditEvent.Action,
            auditEvent.ActorId,
            auditEvent.OccurredAt,
            auditEvent.MetadataJson);
    }
}
