using System.Text.Json;

using Cynara.Application.Common;
using Cynara.Application.Modules.Audit.Persistence;
using Cynara.Domain.Audit;

namespace Cynara.Application.Audit;

public sealed class AuditWriter(IAuditRepository audit) : IAuditWriter
{
    public void Append(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId,
        DateTimeOffset occurredAt,
        object metadata)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            ActorId = actorId,
            OccurredAt = occurredAt,
            MetadataJson = JsonSerializer.Serialize(
                metadata,
                CanonicalJsonOptions.Instance),
        });
    }
}
