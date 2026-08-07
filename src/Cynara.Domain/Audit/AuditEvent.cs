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
    /// <summary>Owning hospital workspace. Stamped by the audit writer.</summary>
    public Guid HospitalId { get; set; }

    [Attr(PublicName = "resourceType", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required string ResourceType { get; set; }

    [Attr(PublicName = "resourceId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public Guid ResourceId { get; set; }

    [Attr(PublicName = "action", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required string Action { get; set; }

    [Attr(PublicName = "actorId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? ActorId { get; set; }

    /// <summary>
    /// Patient the audited activity belongs to, when the resource is patient
    /// or pipeline scoped. Stamped by the audit writer so reviewers can query
    /// events by patient without reading metadata JSON.
    /// </summary>
    [Attr(PublicName = "patientId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public Guid? PatientId { get; set; }

    /// <summary>
    /// Encounter the audited activity belongs to, when the resource is
    /// encounter or pipeline scoped. Stamped by the audit writer so reviewers
    /// can query events by encounter without reading metadata JSON.
    /// </summary>
    [Attr(PublicName = "encounterId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public Guid? EncounterId { get; set; }

    /// <summary>
    /// Workflow definition the audited activity belongs to, for workflow
    /// configuration, pipeline, and task events. Stamped by the audit writer
    /// so reviewers can query all events for a workflow definition without
    /// reading metadata JSON.
    /// </summary>
    [Attr(PublicName = "workflowDefinitionId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public Guid? WorkflowDefinitionId { get; set; }

    [Attr(PublicName = "occurredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset OccurredAt { get; set; }

    [Attr(PublicName = "metadataJson", Capabilities = AttrCapabilities.AllowView)]
    public string? MetadataJson { get; set; }
}
