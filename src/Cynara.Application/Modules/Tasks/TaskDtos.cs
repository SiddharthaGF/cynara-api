using System.Text.Json.Serialization;

namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Public read shape for a clinical task. Provenance (pipeline, node,
/// pinned form code/version) is immutable; <c>Status</c> is open, claimed,
/// completed, or canceled; assignee values are opaque copies from the
/// published workflow definition; RowVersion guards transitions.
/// </summary>
public sealed record TaskDto(
    Guid Id,
    Guid PipelineId,
    Guid WorkflowVersionId,
    string NodeId,
    string Name,
    string? Description,
    string Status,
    string? AssignedActor,
    string? AssignedRole,
    string? AssignedDiscipline,
    Guid PatientId,
    Guid? EncounterId,
    string? FormCode,
    string? FormVersion,
    DateTimeOffset? DueAt,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAt,
    string? CompletedBy,
    DateTimeOffset? CompletedAt,
    string? CanceledBy,
    DateTimeOffset? CanceledAt,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// List filter for tasks. All criteria are optional; an empty filter returns
/// the hospital task roster including terminal states.
/// </summary>
public sealed record TaskListRequest(
    string? Status = null,
    Guid? PatientId = null,
    Guid? EncounterId = null,
    Guid? PipelineId = null,
    string? AssignedActor = null,
    string? AssignedRole = null,
    string? AssignedDiscipline = null);

/// <summary>Collection response for task listings.</summary>
public sealed record TaskListResponse(
    IReadOnlyList<TaskDto> Tasks);

/// <summary>
/// Claim contract for an open task. The <c>RowVersion</c> must match the
/// latest persisted value.
/// </summary>
public sealed record ClaimTaskRequest(
    [property: JsonRequired] uint RowVersion);

/// <summary>
/// Lifecycle contract for completing or canceling a task. The
/// <c>RowVersion</c> must match; optional <c>Reason</c> is recorded in the
/// audit event.
/// </summary>
public sealed record TransitionTaskRequest(
    [property: JsonRequired] uint RowVersion,
    string? Reason = null);
