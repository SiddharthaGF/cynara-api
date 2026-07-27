namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Public read and write shapes for the patient registry.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>Mrn</c> – medical record number as supplied by the caller;
/// stored verbatim.</item>
/// <item><c>NationalId</c> – optional national identifier (passport,
/// government ID); trimmed at write time.</item>
/// <item><c>GivenName</c> / <c>FamilyName</c> – trimmed at write time.</item>
/// <item><c>BirthDate</c> – UTC date of birth (no time zone).</item>
/// <item><c>Sex</c> – one of <c>female</c>, <c>male</c>, <c>unknown</c>.</item>
/// <item><c>Status</c> – one of <c>active</c>, <c>retired</c>.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// PATCH and soft-delete.</item>
/// <item><c>DeletedAt</c> – UTC timestamp when the patient was
/// soft-deleted; <see langword="null"/> while active.</item>
/// <item><c>CreatedAt</c> / <c>UpdatedAt</c> – UTC timestamps.</item>
/// </list>
/// </remarks>
public sealed record PatientDto(
    Guid Id,
    string Mrn,
    string? NationalId,
    string GivenName,
    string FamilyName,
    DateOnly BirthDate,
    string Sex,
    string Status,
    uint RowVersion,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Create contract for patients. The MRN is the business identifier within
/// the resolved hospital workspace and must be unique across the hospital;
/// the <c>Sex</c> value is stored in lowercase.
/// </summary>
public sealed record CreatePatientRequest(
    string Mrn,
    string? NationalId,
    string GivenName,
    string FamilyName,
    DateOnly BirthDate,
    string Sex);

/// <summary>
/// Update contract for mutable demographic fields. The MRN is immutable
/// after creation; clients must delete and recreate to re-issue a new MRN.
/// </summary>
public sealed record UpdatePatientRequest(
    string? NationalId,
    string GivenName,
    string FamilyName,
    DateOnly BirthDate,
    string Sex,
    uint RowVersion);

/// <summary>
/// Soft-delete contract for a patient. The <c>RowVersion</c> must match
/// the stored value; the row is hidden from default search and detail
/// responses but remains resolvable for historical form responses.
/// </summary>
public sealed record SoftDeletePatientRequest(uint RowVersion);

/// <summary>
/// Search filter contract for the patient registry. All criteria are
/// optional; an empty filter returns the active, non-deleted roster for
/// the resolved hospital workspace.
/// </summary>
public sealed record PatientSearchRequest(
    string? Mrn,
    string? NationalId,
    string? GivenName,
    string? FamilyName,
    bool IncludeDeleted = false);

/// <summary>JSON-API-free collection response for patient listings.</summary>
public sealed record PatientListResponse(IReadOnlyList<PatientDto> Patients);
