using System.ComponentModel.DataAnnotations;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.ClinicalTaxonomy;

/// <summary>
/// Tenant-owned clinical area or department. Clinical areas are scoped to a
/// facility so the hierarchy reads as Facility → ClinicalArea → Discipline.
/// </summary>
[Resource(
    PublicName = "clinicalAreas",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class ClinicalArea : Identifiable<Guid>
{
    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>Stable business code unique within the hospital workspace.</summary>
    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    /// <summary>Human-readable clinical area name.</summary>
    [Attr(PublicName = "name")]
    public required string Name { get; set; }

    /// <summary>Lifecycle status of the clinical area.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public ClinicalTaxonomyStatus Status { get; set; }

    /// <summary>UTC timestamp when the clinical area was created.</summary>
    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last clinical area metadata change.</summary>
    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>UTC timestamp when the clinical area was retired, if applicable.</summary>
    [Attr(PublicName = "retiredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on PATCH,
    /// mismatch returns 409.
    /// </summary>
    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }

    /// <summary>Owning facility. Required; clinical areas cannot float.</summary>
    public Guid FacilityId { get; set; }

    [HasOne(PublicName = "facility")]
    public Facility Facility { get; set; } = null!;

    /// <summary>Disciplines practiced within this clinical area.</summary>
    [HasMany(PublicName = "disciplines")]
    public ISet<Discipline> Disciplines { get; set; } =
        new HashSet<Discipline>();

    /// <summary>Clinical area code constraints applied at the application boundary.</summary>
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
                    $"Clinical area code '{code}' must be {MinLength}-{MaxLength} characters.");
            }
        }
    }
}
