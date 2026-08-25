using Cynara.Domain.Common;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Workflows;

/// <summary>
/// Clinical workflow version with draft/review lifecycle. The graph JSON
/// payload is validated before review and publish transitions; published
/// snapshots are immutable and remain readable after retirement.
/// </summary>
[Resource(
    PublicName = "workflowVersions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class WorkflowVersion
    : Identifiable<Guid>, IHospitalScopedResource
{
    /// <summary>Owning hospital workspace (denormalized from the definition).</summary>
    public Guid HospitalId { get; set; }

    /// <summary>FK to the owning definition (infrastructure; use relationship).</summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>Owning workflow definition.</summary>
    [HasOne(PublicName = "workflowDefinition")]
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    /// <summary>Semver label assigned at publish; null while draft/review.</summary>
    [Attr(PublicName = "version", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? Version { get; set; }

    /// <summary>Lifecycle status: draft, review, published, or retired.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public WorkflowVersionStatus Status { get; set; }

    /// <summary>Workflow graph schema document (JSON string).</summary>
    [Attr(PublicName = "workflowSchemaJson")]
    public required string WorkflowSchemaJson { get; set; }

    /// <summary>Content hash computed at publish time.</summary>
    [Attr(PublicName = "contentHash", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? ContentHash { get; set; }

    /// <summary>Optimistic concurrency token for draft/review mutations.</summary>
    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }

    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    [Attr(PublicName = "submittedForReviewAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? SubmittedForReviewAt { get; set; }

    [Attr(PublicName = "publishedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? PublishedAt { get; set; }

    [Attr(PublicName = "retiredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? RetiredAt { get; set; }

    [Attr(PublicName = "publishedSchemaVersion", Capabilities = AttrCapabilities.AllowView)]
    public string? PublishedSchemaVersion { get; set; }

    [Attr(PublicName = "lastReviewComment", Capabilities = AttrCapabilities.AllowView)]
    public string? LastReviewComment { get; set; }

    [Attr(PublicName = "lastReviewDecision", Capabilities = AttrCapabilities.AllowView)]
    public string? LastReviewDecision { get; set; }

    [Attr(PublicName = "lastReviewedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? LastReviewedAt { get; set; }
}
