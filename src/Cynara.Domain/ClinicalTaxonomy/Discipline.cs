using System.ComponentModel.DataAnnotations;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.ClinicalTaxonomy;

/// <summary>
/// Tenant-owned discipline or specialty. Disciplines are scoped to a clinical
/// area so encounters can be classified by Facility → ClinicalArea → Discipline.
/// </summary>
[Resource(
    PublicName = "disciplines",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class Discipline : Identifiable<Guid>
{
    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>Stable business code unique within the hospital workspace.</summary>
    [Attr(PublicName = "code")]
    public required string Code { get; set; }

    /// <summary>Human-readable discipline name.</summary>
    [Attr(PublicName = "name")]
    public required string Name { get; set; }

    /// <summary>Lifecycle status of the discipline.</summary>
    [Attr(PublicName = "status", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public ClinicalTaxonomyStatus Status { get; set; }

    /// <summary>UTC timestamp when the discipline was created.</summary>
    [Attr(PublicName = "createdAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last discipline metadata change.</summary>
    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>UTC timestamp when the discipline was retired, if applicable.</summary>
    [Attr(PublicName = "retiredAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on PATCH,
    /// mismatch returns 409.
    /// </summary>
    [Attr(PublicName = "rowVersion")]
    public uint RowVersion { get; set; }

    /// <summary>Owning clinical area. Required; disciplines cannot float.</summary>
    public Guid ClinicalAreaId { get; set; }

    [HasOne(PublicName = "clinicalArea")]
    public ClinicalArea ClinicalArea { get; set; } = null!;

    /// <summary>Discipline code constraints applied at the application boundary.</summary>
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
                    $"Discipline code '{code}' must be {MinLength}-{MaxLength} characters.");
            }
        }
    }
}
