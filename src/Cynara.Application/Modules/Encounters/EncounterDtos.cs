using Cynara.Application.OpenApi;

namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Public read and write shapes for clinical encounters.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>PatientId</c> / <c>FacilityId</c> / <c>ClinicalAreaId</c> –
/// organizational references; immutable after creation.</item>
/// <item><c>Type</c> – one of ambulatory, emergency, inpatient,
/// observation, or virtual.</item>
/// <item><c>ResponsibleProfessionalId</c> – actor-style identifier;
/// trimmed at write time.</item>
/// <item><c>Status</c> – one of <c>open</c>, <c>completed</c>,
/// <c>canceled</c>, <c>enteredInError</c>.</item>
/// <item><c>StartedAt</c> / <c>EndedAt</c> – UTC timestamps.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// transitions.</item>
/// </list>
/// </remarks>
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
