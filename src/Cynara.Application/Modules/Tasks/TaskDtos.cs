namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Public read and write shapes for clinical tasks.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>PipelineId</c> – the pipeline that generated the task; immutable.</item>
/// <item><c>NodeId</c> – the workflow node that generated the task; immutable.</item>
/// <item><c>Status</c> – one of <c>open</c>, <c>claimed</c>,
/// <c>completed</c>, <c>canceled</c>.</item>
/// <item><c>AssignedActor</c> / <c>AssignedRole</c> / <c>AssignedDiscipline</c>
/// – opaque assignee values from the published workflow definition.</item>
/// <item><c>FormCode</c> / <c>FormVersion</c> – the referenced clinical
/// document; immutable snapshot of the pinned definition.</item>
/// <item><c>DueAt</c> – optional due timestamp derived from the node's
/// dueDays at generation time.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// transitions.</item>
/// </list>
/// </remarks>
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
public sealed record ClaimTaskRequest(uint RowVersion);

/// <summary>
/// Lifecycle contract for completing or canceling a task. The
/// <c>RowVersion</c> must match; optional <c>Reason</c> is recorded in the
/// audit event.
/// </summary>
public sealed record TransitionTaskRequest(
    uint RowVersion,
    string? Reason = null);
