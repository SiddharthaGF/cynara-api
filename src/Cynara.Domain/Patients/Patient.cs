using System.Data;

namespace Cynara.Domain.Patients;

/// <summary>
/// Tenant-scoped patient registry entry. Each patient belongs to exactly
/// one hospital workspace (resolved from <c>IHospitalContext</c>) and is
/// identified within that workspace by an upper-cased MRN. National ID is
/// optional but, when present, is also normalized for cross-field
/// uniqueness checks. Records can be soft-deleted: deletions only set
/// <see cref="DeletedAt"/>; the row remains resolvable for historical
/// form responses and audit continuity.
/// </summary>
public sealed class Patient
{
    /// <summary>Surrogate identifier; immutable.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>
    /// Medical record number as captured from the request. Stored verbatim
    /// for display but never used for uniqueness or search comparisons.
    /// </summary>
    public required string Mrn { get; set; }

    /// <summary>
    /// Upper-cased, trimmed MRN used by the unique index and search
    /// comparisons. Computed at the workflow boundary.
    /// </summary>
    public required string NormalizedMrn { get; set; }

    /// <summary>
    /// Optional national identifier (passport, government ID). When
    /// present, stored trimmed as captured.
    /// </summary>
    public string? NationalId { get; set; }

    /// <summary>
    /// Trimmed upper-cased national identifier used by the search index.
    /// <see langword="null"/> when no national identifier was supplied.
    /// </summary>
    public string? NormalizedNationalId { get; set; }

    /// <summary>Given (first) name; trimmed at write time.</summary>
    public required string GivenName { get; set; }

    /// <summary>Trimmed upper-cased given name used by the search index.</summary>
    public required string NormalizedGivenName { get; set; }

    /// <summary>Family (last) name; trimmed at write time.</summary>
    public required string FamilyName { get; set; }

    /// <summary>Trimmed upper-cased family name used by the search index.</summary>
    public required string NormalizedFamilyName { get; set; }

    /// <summary>UTC date of birth (date component only).</summary>
    public DateOnly BirthDate { get; set; }

    /// <summary>Patient biological sex.</summary>
    public Sex Sex { get; set; }

    /// <summary>Lifecycle status of the patient record.</summary>
    public PatientStatus Status { get; set; }

    /// <summary>UTC timestamp when the patient was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last patient metadata change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>UTC timestamp when the patient was soft-deleted; immutable after deletion.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on PATCH.
    /// Mismatch returns <see cref="DBConcurrencyException"/>.
    /// </summary>
    public uint RowVersion { get; set; }
}
