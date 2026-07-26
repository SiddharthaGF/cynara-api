using System.ComponentModel.DataAnnotations;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.ClinicalTaxonomy;

/// <summary>
/// Tenant-owned facility/site definition. Identified by a stable
/// <see cref="Code"/> unique within the hospital workspace; the surrogate
/// <c>Id</c> drives relationships and foreign keys.
/// </summary>
[Resource(
    PublicName = "facilities",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class Facility : Identifiable<Guid>
{
    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>Stable business code used by clients and URLs.</summary>
    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    /// <summary>Human-readable facility/site name.</summary>
    [Attr(PublicName = "name")]
    public required string Name { get; set; }

    /// <summary>Lifecycle status of the facility.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public ClinicalTaxonomyStatus Status { get; set; }

    /// <summary>UTC timestamp when the facility was created.</summary>
    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last facility metadata change.</summary>
    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>UTC timestamp when the facility was retired, if applicable.</summary>
    [Attr(PublicName = "retiredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on PATCH,
    /// mismatch returns 409.
    /// </summary>
    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }

    /// <summary>
    /// Child clinical areas belonging to this facility. Exposed over JSON:API
    /// via the relationships inclusion.
    /// </summary>
    [HasMany(PublicName = "clinicalAreas")]
    public ISet<ClinicalArea> ClinicalAreas { get; set; } =
        new HashSet<ClinicalArea>();

    /// <summary>Facility code constraints applied at the application boundary.</summary>
    public static class Codes
    {
        public const int MaxLength = 64;
        public const int MinLength = 1;
        public const string Pattern = "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,62}[a-zA-Z0-9]$";

        public static void EnsureValid(string code)
        {
            if (string.IsNullOrWhiteSpace(code)
                || code.Length < MinLength
                || code.Length > MaxLength)
            {
                throw new ValidationException(
                    $"Facility code '{code}' must be {MinLength}-{MaxLength} characters.");
            }
        }
    }
}
