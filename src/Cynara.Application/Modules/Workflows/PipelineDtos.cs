using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Public read shape for a workflow pipeline. The pinned published workflow
/// (code/version/schema) is immutable after start; subject type is
/// encounter or patient; status is running/completed/canceled/
/// enteredInError; RowVersion guards transitions.
/// </summary>
public sealed record PipelineDto(
    Guid Id,
    string WorkflowCode,
    string WorkflowVersion,
    Guid WorkflowVersionId,
    string WorkflowSchemaVersion,
    string SubjectType,
    Guid SubjectId,
    Guid PatientId,
    Guid? EncounterId,
    string Status,
    string CurrentNodeId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One append-only progression event on a pipeline.
/// </summary>
public sealed record PipelineHistoryDto(
    Guid Id,
    Guid PipelineId,
    int Sequence,
    string Action,
    string? ActorId,
    DateTimeOffset OccurredAt,
    string? MetadataJson);

/// <summary>
/// Start contract for a pipeline. Resolves the published workflow version
/// (the supplied semver, or the latest published when omitted) within the
/// resolved hospital and pins it for the pipeline lifetime.
/// </summary>
public sealed record StartPipelineRequest(
    [property: JsonRequired] string WorkflowCode,
    string? WorkflowVersion,
    [property: JsonRequired] string SubjectType,
    [property: JsonRequired] Guid SubjectId);

/// <summary>
/// Advance contract for a running pipeline. The server evaluates the
/// outgoing transition guards/conditions against <see cref="InputValues"/>
/// (declared workflow inputs) and picks the branch; clients cannot choose
/// the next node directly.
/// </summary>
public sealed record AdvancePipelineRequest(
    [property: JsonRequired] uint RowVersion,
    IReadOnlyDictionary<string, JsonElement>? InputValues = null);

/// <summary>
/// Lifecycle contract for completing, canceling, or entering a pipeline in
/// error. The <c>RowVersion</c> must match; optional <c>Reason</c> is
/// recorded in the progression history.
/// </summary>
public sealed record TransitionPipelineRequest(
    [property: JsonRequired] uint RowVersion,
    string? Reason = null);

/// <summary>
/// List filter for pipelines. All criteria are optional; an empty filter
/// returns the hospital roster including terminal states.
/// </summary>
public sealed record PipelineListRequest(
    string? SubjectType = null,
    Guid? SubjectId = null,
    string? Status = null,
    Guid? PatientId = null,
    Guid? EncounterId = null);

/// <summary>Collection response for pipeline listings.</summary>
public sealed record PipelineListResponse(
    IReadOnlyList<PipelineDto> Pipelines);

/// <summary>Append-only progression history response for one pipeline.</summary>
public sealed record PipelineHistoryResponse(
    Guid PipelineId,
    IReadOnlyList<PipelineHistoryDto> History);

/// <summary>
/// Projection of one node of the pinned workflow graph. Conditions are
/// omitted; the branch taken is recorded in the progression history.
/// </summary>
public sealed record WorkflowNodeDto(
    string Id,
    string Type,
    string? Name);

/// <summary>
/// Projection of one edge of the pinned workflow graph. Transition
/// conditions are intentionally not exposed to clients.
/// </summary>
public sealed record WorkflowEdgeDto(
    string From,
    string To,
    string? Label);

/// <summary>
/// The workflow graph exactly as pinned at pipeline start, projected from
/// the immutable published version for historical rendering.
/// </summary>
public sealed record WorkflowGraphDto(
    IReadOnlyList<WorkflowNodeDto> Nodes,
    IReadOnlyList<WorkflowEdgeDto> Edges);

/// <summary>
/// One patient journey: a pipeline bound to a patient or encounter record,
/// rendered from the exact published workflow version at start time with the
/// immutable progression history.
/// </summary>
public sealed record JourneyDto(
    Guid PipelineId,
    string WorkflowCode,
    string WorkflowVersion,
    Guid WorkflowVersionId,
    string WorkflowSchemaVersion,
    string SubjectType,
    Guid SubjectId,
    Guid PatientId,
    Guid? EncounterId,
    string Status,
    string CurrentNodeId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    WorkflowGraphDto Graph,
    IReadOnlyList<PipelineHistoryDto> History);

/// <summary>
/// The full pipeline journey for one patient record, ordered by start time.
/// </summary>
public sealed record PatientJourneyResponse(
    Guid PatientId,
    IReadOnlyList<JourneyDto> Journeys);

/// <summary>
/// The full pipeline journey for one encounter, ordered by start time.
/// </summary>
public sealed record EncounterJourneyResponse(
    Guid EncounterId,
    IReadOnlyList<JourneyDto> Journeys);
