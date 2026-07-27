using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Hospitals;

/// <summary>
/// Tenant aggregate representing the hospital workspace that owns every
/// Stage 2 configuration and clinical record. Codes are stable business
/// identifiers; the surrogate <c>Id</c> is used for foreign keys.
/// </summary>
[Resource(
    PublicName = "hospitals",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class Hospital : Identifiable<Guid>
{
    /// <summary>Stable business code used by clients and URLs.</summary>
    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    /// <summary>Human-readable workspace name.</summary>
    [Attr(PublicName = "name")]
    public required string Name { get; set; }

    /// <summary>Lifecycle status of the workspace.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public HospitalStatus Status { get; set; }

    /// <summary>Optional metadata payload (JSON document).</summary>
    [Attr(PublicName = "metadataJson")]
    public string? MetadataJson { get; set; }

    /// <summary>UTC timestamp when the workspace was created.</summary>
    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last workspace metadata change.</summary>
    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token for workspace updates.</summary>
    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }
}
