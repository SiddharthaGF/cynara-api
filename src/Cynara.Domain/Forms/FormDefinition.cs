using System.ComponentModel.DataAnnotations.Schema;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Forms;

/// <summary>
/// Clinical form catalog entry identified by a stable business <see cref="Code"/>.
/// Soft-deleted definitions are hidden from queries; drafts live on related versions.
/// </summary>
[Resource(
    PublicName = "formDefinitions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class FormDefinition : Identifiable<Guid>
{
    /// <summary>Stable business code used by clients and URLs historically.</summary>
    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    /// <summary>Human-readable form title.</summary>
    [Attr(PublicName = "name")]
    public required string Name { get; set; }

    /// <summary>UTC timestamp when the definition was created.</summary>
    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last definition metadata change.</summary>
    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft-delete marker; not exposed over JSON:API.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Write-only create helper used to seed the first draft clinical schema.
    /// Not persisted on the definition row.
    /// </summary>
    [Attr(PublicName = "initialClinicalSchemaJson", Capabilities = AttrCapabilities.AllowCreate)]
    [NotMapped]
    public string? InitialClinicalSchemaJson { get; set; }

    /// <summary>
    /// Write-only create helper for the first draft UI schema. Not persisted here.
    /// </summary>
    [Attr(PublicName = "initialUiSchemaJson", Capabilities = AttrCapabilities.AllowCreate)]
    [NotMapped]
    public string? InitialUiSchemaJson { get; set; }

    /// <summary>
    /// Write-only create helper for the first draft rules schema. Not persisted here.
    /// </summary>
    [Attr(PublicName = "initialRulesSchemaJson", Capabilities = AttrCapabilities.AllowCreate)]
    [NotMapped]
    public string? InitialRulesSchemaJson { get; set; }

    /// <summary>All versions belonging to this definition.</summary>
    [HasMany(PublicName = "versions")]
    public ISet<FormVersion> Versions { get; set; } = new HashSet<FormVersion>();
}
