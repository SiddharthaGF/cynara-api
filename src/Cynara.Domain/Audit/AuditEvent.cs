using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Audit;

/// <summary>
/// Append-only audit trail entry for mutating workflows. Read-only over JSON:API.
/// </summary>
[Resource(
    PublicName = "auditEvents",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class AuditEvent : Identifiable<Guid>
{
    [Attr(PublicName = "resourceType", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required string ResourceType { get; set; }

    [Attr(PublicName = "resourceId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public Guid ResourceId { get; set; }

    [Attr(PublicName = "action", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required string Action { get; set; }

    [Attr(PublicName = "actorId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? ActorId { get; set; }

    [Attr(PublicName = "occurredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset OccurredAt { get; set; }

    [Attr(PublicName = "metadataJson", Capabilities = AttrCapabilities.AllowView)]
    public string? MetadataJson { get; set; }
}
