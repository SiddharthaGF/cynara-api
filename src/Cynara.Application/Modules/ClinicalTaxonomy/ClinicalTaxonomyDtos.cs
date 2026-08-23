using Cynara.Application.OpenApi;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Public read shape for a facility definition. Code is immutable;
/// status is active or retired; RowVersion guards PATCH and retire.
/// </summary>
public sealed record FacilityDto(
    Guid Id,
    string Code,
    string Name,
    [property: OpenApiEnumValues("active", "retired")]
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
/// Public read shape for a clinical area definition. Code and owning
/// facility are immutable; status is active or retired; RowVersion guards
/// PATCH and retire.
/// </summary>
public sealed record ClinicalAreaDto(
    Guid Id,
    string Code,
    string Name,
    Guid FacilityId,
    [property: OpenApiEnumValues("active", "retired")]
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
/// Public read shape for a discipline definition. Code and owning clinical
/// area are immutable; status is active or retired; RowVersion guards
/// PATCH and retire.
/// </summary>
public sealed record DisciplineDto(
    Guid Id,
    string Code,
    string Name,
    Guid ClinicalAreaId,
    [property: OpenApiEnumValues("active", "retired")]
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
