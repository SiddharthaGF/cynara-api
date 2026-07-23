using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Forms;

/// <summary>
/// Patient/clinician answers captured against a published form version.
/// Soft-deleted drafts are hidden unless explicitly requested by services.
/// </summary>
[Resource(
    PublicName = "formResponses",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class FormResponse : Identifiable<Guid>
{
    /// <summary>FK to form version (infrastructure; use relationship).</summary>
    public Guid FormVersionId { get; set; }

    /// <summary>Published form version this response answers.</summary>
    [HasOne(PublicName = "formVersion")]
    public FormVersion FormVersion { get; set; } = null!;

    /// <summary>Response lifecycle: draft or completed.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public FormResponseStatus Status { get; set; }

    /// <summary>Answers document (JSON string) validated against the clinical schema.</summary>
    [Attr(PublicName = "answersJson")]
    public required string AnswersJson { get; set; }

    /// <summary>Monotonic revision counter for answer history.</summary>
    [Attr(PublicName = "revisionNumber", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public uint RevisionNumber { get; set; }

    /// <summary>Optimistic concurrency token for draft updates.</summary>
    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }

    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    [Attr(PublicName = "completedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? CompletedAt { get; set; }

    [Attr(PublicName = "deletedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Historical answer revisions.</summary>
    [HasMany(PublicName = "revisions")]
    public ISet<FormResponseRevision> Revisions { get; set; } =
        new HashSet<FormResponseRevision>();
}
