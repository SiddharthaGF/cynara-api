namespace Cynara.Domain.Audit;

public sealed class AuditEvent
{
    public Guid Id { get; set; }

    public required string ResourceType { get; set; }

    public Guid ResourceId { get; set; }

    public required string Action { get; set; }

    public string? ActorId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? MetadataJson { get; set; }
}
