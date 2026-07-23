using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Forms;

/// <summary>
/// Immutable-published clinical form version with draft/review lifecycle.
/// Schema JSON payloads are validated before review and publish transitions.
/// </summary>
[Resource(
    PublicName = "formVersions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class FormVersion : Identifiable<Guid>
{
    /// <summary>FK to the owning definition (infrastructure; use relationship).</summary>
    public Guid FormDefinitionId { get; set; }

    /// <summary>Owning form definition.</summary>
    [HasOne(PublicName = "formDefinition")]
    public FormDefinition FormDefinition { get; set; } = null!;

    /// <summary>Semver label assigned at publish; null while draft/review.</summary>
    [Attr(PublicName = "version", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? Version { get; set; }

    /// <summary>Lifecycle status: draft, review, published, or retired.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public FormVersionStatus Status { get; set; }

    /// <summary>Clinical schema document (JSON string).</summary>
    [Attr(PublicName = "clinicalSchemaJson")]
    public required string ClinicalSchemaJson { get; set; }

    /// <summary>Optional UI schema document (JSON string).</summary>
    [Attr(PublicName = "uiSchemaJson")]
    public string? UiSchemaJson { get; set; }

    /// <summary>Optional rules schema document (JSON string).</summary>
    [Attr(PublicName = "rulesSchemaJson")]
    public string? RulesSchemaJson { get; set; }

    /// <summary>Content hash computed at publish time.</summary>
    [Attr(PublicName = "contentHash", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? ContentHash { get; set; }

    /// <summary>Resolved component dependency metadata after compile.</summary>
    [Attr(PublicName = "dependencyMetadataJson", Capabilities = AttrCapabilities.AllowView)]
    public string? DependencyMetadataJson { get; set; }

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
