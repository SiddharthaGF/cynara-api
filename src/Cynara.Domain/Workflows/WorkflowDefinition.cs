using System.ComponentModel.DataAnnotations.Schema;

using Cynara.Domain.Common;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Workflows;

/// <summary>
/// Clinical workflow catalog entry identified by a stable business <see cref="Code"/>.
/// Soft-deleted definitions are hidden from queries; drafts live on related versions.
/// </summary>
[Resource(
    PublicName = "workflowDefinitions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class WorkflowDefinition
    : Identifiable<Guid>, IHospitalScopedResource
{
    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>Stable business code used by clients and URLs historically.</summary>
    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    /// <summary>Human-readable workflow title.</summary>
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
    /// Write-only create helper used to seed the first draft workflow graph.
    /// Not persisted on the definition row.
    /// </summary>
    [Attr(PublicName = "initialWorkflowSchemaJson", Capabilities = AttrCapabilities.AllowCreate)]
    [NotMapped]
    public string? InitialWorkflowSchemaJson { get; set; }

    /// <summary>All versions belonging to this definition.</summary>
    [HasMany(PublicName = "versions")]
    public ISet<WorkflowVersion> Versions { get; set; } = new HashSet<WorkflowVersion>();
}
