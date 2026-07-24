using System.ComponentModel.DataAnnotations;

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

    /// <summary>Hospital code constraints applied at the application boundary.</summary>
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
                    $"Hospital code '{code}' must be {MinLength}-{MaxLength} characters.");
            }
        }
    }
}
