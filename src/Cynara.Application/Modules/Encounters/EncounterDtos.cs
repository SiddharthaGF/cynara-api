using Cynara.Application.OpenApi;

namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Public read shape for a clinical encounter. Patient, facility, and area
/// references are immutable after creation; <c>Status</c> is open,
/// completed, canceled, or enteredInError; RowVersion guards transitions.
/// </summary>
public sealed record EncounterDto(
    Guid Id,
    Guid PatientId,
    Guid FacilityId,
    Guid ClinicalAreaId,
    [property: OpenApiEnumValues("ambulatory", "emergency", "inpatient", "observation", "virtual")]
    string Type,
    string ResponsibleProfessionalId,
    [property: OpenApiEnumValues("open", "completed", "canceled", "enteredInError")]
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Create contract for encounters. References must resolve within the
/// hospital workspace and must not be retired or soft-deleted.
/// </summary>
public sealed record CreateEncounterRequest(
    Guid PatientId,
    Guid FacilityId,
    Guid ClinicalAreaId,
    string Type,
    string ResponsibleProfessionalId,
    DateTimeOffset? StartedAt = null);

/// <summary>
/// Transition contract for completing, canceling, or entering an encounter
/// in error. The <c>RowVersion</c> must match; optional <c>EndedAt</c>
/// defaults to the workflow clock when omitted.
/// </summary>
public sealed record TransitionEncounterRequest(
    uint RowVersion,
    DateTimeOffset? EndedAt = null);

/// <summary>
/// List filter for encounters. All criteria are optional; an empty filter
/// returns the full hospital roster including terminal states.
/// </summary>
public sealed record EncounterListRequest(
    Guid? PatientId = null,
    Guid? FacilityId = null,
    Guid? ClinicalAreaId = null,
    string? Status = null);

/// <summary>JSON-API-free collection response for encounter listings.</summary>
public sealed record EncounterListResponse(
    IReadOnlyList<EncounterDto> Encounters);
