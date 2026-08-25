using Cynara.Domain.Common;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Documents;

/// <summary>
/// Catalog entry configuring which clinical documents a hospital can create
/// from a published form definition. Each entry pins exactly one published
/// form version so historical documents keep resolving against the version
/// they were started on.
/// </summary>
[Resource(
    PublicName = "documentDefinitions",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class DocumentDefinition
    : Identifiable<Guid>, IHospitalScopedResource
{
    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>Stable business code unique within the hospital workspace.</summary>
    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    /// <summary>Human-readable document definition name.</summary>
    [Attr(PublicName = "name")]
    public required string Name { get; set; }

    /// <summary>Lifecycle status of the catalog entry.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DocumentDefinitionStatus Status { get; set; }

    /// <summary>FK to the form definition backing the document.</summary>
    public Guid FormDefinitionId { get; set; }

    /// <summary>Owning form definition.</summary>
    [HasOne(PublicName = "formDefinition")]
    public Forms.FormDefinition FormDefinition { get; set; } = null!;

    /// <summary>
    /// FK to the exact published form version backing the document. The
    /// identifier is captured at creation time and preserved through
    /// retirement so historical documents remain resolvable.
    /// </summary>
    public Guid FormVersionId { get; set; }

    /// <summary>Exact published form version backing the document.</summary>
    [HasOne(PublicName = "formVersion")]
    public Forms.FormVersion FormVersion { get; set; } = null!;

    /// <summary>FK to the owning facility.</summary>
    public Guid FacilityId { get; set; }

    /// <summary>Owning facility.</summary>
    [HasOne(PublicName = "facility")]
    public ClinicalTaxonomy.Facility Facility { get; set; } = null!;

    /// <summary>FK to the owning clinical area.</summary>
    public Guid ClinicalAreaId { get; set; }

    /// <summary>Owning clinical area.</summary>
    [HasOne(PublicName = "clinicalArea")]
    public ClinicalTaxonomy.ClinicalArea ClinicalArea { get; set; } = null!;

    /// <summary>FK to the owning discipline.</summary>
    public Guid DisciplineId { get; set; }

    /// <summary>Owning discipline.</summary>
    [HasOne(PublicName = "discipline")]
    public ClinicalTaxonomy.Discipline Discipline { get; set; } = null!;

    /// <summary>
    /// Whether multiple document instances may be created for the same
    /// encounter under this catalog entry.
    /// </summary>
    [Attr(PublicName = "allowsMultipleInstancesPerEncounter")]
    public bool AllowsMultipleInstancesPerEncounter { get; set; } = true;

    /// <summary>
    /// Whether an authenticated actor is required to start a document
    /// instance from this catalog entry.
    /// </summary>
    [Attr(PublicName = "requiresActorForCreation")]
    public bool RequiresActorForCreation { get; set; } = true;

    /// <summary>
    /// Whether an authenticated actor is required to complete a document
    /// instance started from this catalog entry.
    /// </summary>
    [Attr(PublicName = "requiresActorForCompletion")]
    public bool RequiresActorForCompletion { get; set; } = true;

    /// <summary>UTC timestamp when the catalog entry was created.</summary>
    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last catalog entry metadata change.</summary>
    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>UTC timestamp when the catalog entry was retired.</summary>
    [Attr(PublicName = "retiredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on PATCH,
    /// mismatch returns 409.
    /// </summary>
    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }
}
