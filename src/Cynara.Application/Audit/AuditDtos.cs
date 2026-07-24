namespace Cynara.Application.Audit;

public sealed record AuditEventDto(
    Guid Id,
    string ResourceType,
    Guid ResourceId,
    string Action,
    string? ActorId,
    DateTimeOffset OccurredAt,
    string? MetadataJson);

public sealed record AuditQuery(
    string? ResourceType = null,
    Guid? ResourceId = null,
    string? ActorId = null,
    int Limit = 50);
