using Cynara.Application.OpenApi;

namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Public read shape for a patient registry row. <c>Mrn</c> is stored
/// verbatim; names and national ID are trimmed at write time; status is
/// active/retired with <c>DeletedAt</c> null while active; RowVersion
/// guards PATCH and soft-delete.
/// </summary>
public sealed record PatientDto(
    Guid Id,
    string Mrn,
    string? NationalId,
    string GivenName,
    string FamilyName,
    DateOnly BirthDate,
    [property: OpenApiEnumValues("female", "male", "unknown")]
    string Sex,
    [property: OpenApiEnumValues("a+", "a-", "b+", "b-", "ab+", "ab-", "o+", "o-")]
    string BloodType,
    [property: OpenApiEnumValues("active", "retired")]
    string Status,
    uint RowVersion,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Create contract for patients. The MRN is the business identifier within
/// the resolved hospital workspace and must be unique across the hospital;
/// sex and blood type values use lowercase clinical notation.
/// </summary>
public sealed record CreatePatientRequest(
    string Mrn,
    string? NationalId,
    string GivenName,
    string FamilyName,
    DateOnly BirthDate,
    string Sex,
    string BloodType);

/// <summary>
/// Update contract for mutable demographic fields. The MRN is immutable
/// after creation; clients must delete and recreate to re-issue a new MRN.
/// Blood type is editable alongside the other demographic fields.
/// </summary>
public sealed record UpdatePatientRequest(
    string? NationalId,
    string GivenName,
    string FamilyName,
    DateOnly BirthDate,
    string Sex,
    string BloodType,
    uint RowVersion);

/// <summary>
/// Soft-delete contract for a patient. The <c>RowVersion</c> must match
/// the stored value; the row is hidden from default search and detail
/// responses but remains resolvable for historical form responses.
/// </summary>
public sealed record SoftDeletePatientRequest(uint RowVersion);

/// <summary>
/// Search filter contract for the patient registry. All criteria optional;
/// an empty filter returns the active, non-deleted roster. Name filters are
/// tokenized and matched diacritic-folded against the full normalized name;
/// MRN and national ID stay exact. Page is 1-based; PageSize is clamped.
/// </summary>
public sealed record PatientSearchRequest(
    string? Mrn,
    string? NationalId,
    string? GivenName,
    string? FamilyName,
    bool IncludeDeleted = false,
    int Page = 1,
    int PageSize = PatientFieldLimits.DefaultPageSize);

/// <summary>
/// JSON-API-free collection response for patient listings, including
/// pagination metadata for the requested page.
/// </summary>
public sealed record PatientListResponse(
    IReadOnlyList<PatientDto> Patients,
    int Page,
    int PageSize,
    int TotalCount);
