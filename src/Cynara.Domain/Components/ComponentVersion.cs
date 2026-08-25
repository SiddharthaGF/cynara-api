using Cynara.Domain.Common;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Components;

/// <summary>
/// Component version with draft → published → retired lifecycle (no review gate).
/// </summary>
[Resource(
    PublicName = "componentVersions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class ComponentVersion
    : Identifiable<Guid>, IHospitalScopedResource
{
    /// <summary>Owning hospital workspace (denormalized from the definition).</summary>
    public Guid HospitalId { get; set; }

    public Guid ComponentDefinitionId { get; set; }

    [HasOne(PublicName = "componentDefinition")]
    public ComponentDefinition ComponentDefinition { get; set; } = null!;

    [Attr(PublicName = "version", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? Version { get; set; }

    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public ComponentVersionStatus Status { get; set; }

    [Attr(PublicName = "clinicalSchemaJson")]
    public required string ClinicalSchemaJson { get; set; }

    [Attr(PublicName = "uiSchemaJson")]
    public string? UiSchemaJson { get; set; }

    [Attr(PublicName = "contentHash", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public string? ContentHash { get; set; }

    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }

    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    [Attr(PublicName = "publishedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? PublishedAt { get; set; }

    [Attr(PublicName = "retiredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? RetiredAt { get; set; }
}
