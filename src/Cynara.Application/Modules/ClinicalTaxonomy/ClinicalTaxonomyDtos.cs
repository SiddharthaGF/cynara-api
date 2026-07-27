namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Public read and write shapes for facility definitions.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>Code</c> – stable business code (pattern
/// <c>^[a-zA-Z0-9][a-zA-Z0-9._-]{0,62}[a-zA-Z0-9]$</c>); immutable.</item>
/// <item><c>Name</c> – human-readable facility name; mutable.</item>
/// <item><c>Status</c> – one of <c>active</c>, <c>retired</c>.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// PATCH/retire.</item>
/// <item><c>RetiredAt</c> – UTC timestamp set when the facility is
/// retired; immutable after retirement.</item>
/// <item><c>CreatedAt</c> – UTC timestamp; immutable.</item>
/// <item><c>UpdatedAt</c> – UTC timestamp of the last metadata change.</item>
/// </list>
/// </remarks>
public sealed record FacilityDto(
    Guid Id,
    string Code,
    string Name,
    string Status,
    uint RowVersion,
    DateTimeOffset? RetiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Create contract for facilities. Code is immutable after creation.</summary>
public sealed record CreateFacilityRequest(
    string Code,
    string Name);

/// <summary>
/// Update contract for mutable display fields. Only the name and a
/// concurrency token are accepted.
/// </summary>
public sealed record UpdateFacilityRequest(
    string Name,
    uint RowVersion);

/// <summary>Retire contract for a facility; the rowVersion must match.</summary>
public sealed record RetireFacilityRequest(uint RowVersion);

/// <summary>
/// Public read and write shapes for clinical area definitions.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>Code</c> – stable business code unique within the hospital;
/// immutable.</item>
/// <item><c>Name</c> – human-readable clinical area name; mutable.</item>
/// <item><c>FacilityId</c> – owning facility identifier; immutable.</item>
/// <item><c>Status</c> – one of <c>active</c>, <c>retired</c>.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// PATCH/retire.</item>
/// <item><c>RetiredAt</c> – UTC timestamp when the area was retired.</item>
/// <item><c>CreatedAt</c> – UTC timestamp; immutable.</item>
/// <item><c>UpdatedAt</c> – UTC timestamp of the last metadata change.</item>
/// </list>
/// </remarks>
public sealed record ClinicalAreaDto(
    Guid Id,
    string Code,
    string Name,
    Guid FacilityId,
    string Status,
    uint RowVersion,
    DateTimeOffset? RetiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Create contract for clinical areas. Facility identifier is required.</summary>
public sealed record CreateClinicalAreaRequest(
    string Code,
    string Name,
    Guid FacilityId);

/// <summary>
/// Update contract for mutable display fields. Ownership cannot be moved
/// by PATCH; clients must delete and recreate to re-parent the row.
/// </summary>
public sealed record UpdateClinicalAreaRequest(
    string Name,
    uint RowVersion);

/// <summary>Retire contract for a clinical area.</summary>
public sealed record RetireClinicalAreaRequest(uint RowVersion);

/// <summary>
/// Public read and write shapes for discipline definitions.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>Code</c> – stable business code unique within the hospital;
/// immutable.</item>
/// <item><c>Name</c> – human-readable discipline name; mutable.</item>
/// <item><c>ClinicalAreaId</c> – owning clinical area identifier; immutable.</item>
/// <item><c>Status</c> – one of <c>active</c>, <c>retired</c>.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// PATCH/retire.</item>
/// <item><c>RetiredAt</c> – UTC timestamp when the discipline was retired.</item>
/// <item><c>CreatedAt</c> – UTC timestamp; immutable.</item>
/// <item><c>UpdatedAt</c> – UTC timestamp of the last metadata change.</item>
/// </list>
/// </remarks>
public sealed record DisciplineDto(
    Guid Id,
    string Code,
    string Name,
    Guid ClinicalAreaId,
    string Status,
    uint RowVersion,
    DateTimeOffset? RetiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Create contract for disciplines. Clinical area identifier is required.</summary>
public sealed record CreateDisciplineRequest(
    string Code,
    string Name,
    Guid ClinicalAreaId);

/// <summary>
/// Update contract for mutable display fields. Ownership cannot be moved
/// by PATCH; clients must delete and recreate to re-parent the row.
/// </summary>
public sealed record UpdateDisciplineRequest(
    string Name,
    uint RowVersion);

/// <summary>Retire contract for a discipline.</summary>
public sealed record RetireDisciplineRequest(uint RowVersion);
