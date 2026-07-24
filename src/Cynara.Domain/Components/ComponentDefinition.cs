using System.ComponentModel.DataAnnotations.Schema;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Components;

/// <summary>
/// Reusable clinical component catalog entry identified by a stable <see cref="Code"/>.
/// </summary>
[Resource(
    PublicName = "componentDefinitions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class ComponentDefinition : Identifiable<Guid>
{
    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    [Attr(PublicName = "name")]
    public required string Name { get; set; }

    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft-delete marker; not exposed over JSON:API.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    [Attr(PublicName = "initialClinicalSchemaJson", Capabilities = AttrCapabilities.AllowCreate)]
    [NotMapped]
    public string? InitialClinicalSchemaJson { get; set; }

    [Attr(PublicName = "initialUiSchemaJson", Capabilities = AttrCapabilities.AllowCreate)]
    [NotMapped]
    public string? InitialUiSchemaJson { get; set; }

    [HasMany(PublicName = "versions")]
    public ISet<ComponentVersion> Versions { get; set; } =
        new HashSet<ComponentVersion>();
}
