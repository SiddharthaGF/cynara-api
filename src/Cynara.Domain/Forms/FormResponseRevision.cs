using Cynara.Domain.Common;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Forms;

/// <summary>
/// Immutable snapshot of a form response after each successful mutation.
/// Revisions are read-only over JSON:API.
/// </summary>
[Resource(
    PublicName = "formResponseRevisions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class FormResponseRevision
    : Identifiable<Guid>, IHospitalScopedResource
{
    /// <summary>Owning hospital workspace (denormalized from the parent response).</summary>
    public Guid HospitalId { get; set; }

    /// <summary>FK to parent response (infrastructure; use relationship).</summary>
    public Guid FormResponseId { get; set; }

    /// <summary>Parent form response.</summary>
    [HasOne(PublicName = "formResponse")]
    public FormResponse FormResponse { get; set; } = null!;

    [Attr(PublicName = "revisionNumber", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public uint RevisionNumber { get; set; }

    [Attr(PublicName = "answersJson", Capabilities = AttrCapabilities.AllowView)]
    public required string AnswersJson { get; set; }

    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public FormResponseStatus Status { get; set; }

    [Attr(PublicName = "actorId", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? ActorId { get; set; }

    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }
}
