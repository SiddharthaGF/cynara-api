using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows.Persistence;

/// <summary>
/// Persistence port for workflow pipelines and their append-only history.
/// All reads are hospital-scoped and include the pinned workflow version so
/// mappers project code/semver without extra round trips; writes return
/// tracked entities and never commit — the workflow owns that boundary.
/// </summary>
public interface IPipelineRepository
{
    /// <summary>
    /// Returns the pipeline matching the supplied identifier in the
    /// resolved hospital workspace, or <see langword="null"/> when none
    /// exists; terminal states stay resolvable. Tracked reads include the
    /// pinned version, definition, and history for one-round-trip advances.
    /// </summary>
    public Task<Pipeline?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists pipelines in the resolved hospital workspace that match the
    /// supplied filter. Terminal states are included by default.
    /// </summary>
    public Task<IReadOnlyList<Pipeline>> ListAsync(
        Guid hospitalId,
        PipelineListCriteria criteria,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists pipelines for a patient or encounter journey in the resolved
    /// hospital workspace. Reads are untracked and include the pinned
    /// workflow version, its definition, and the full append-only history so
    /// journeys can render the exact published graph and progression.
    /// </summary>
    public Task<IReadOnlyList<Pipeline>> ListForJourneyAsync(
        Guid hospitalId,
        PipelineListCriteria criteria,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the append-only progression history for one pipeline in
    /// sequence order. History cannot be filtered or mutated through this
    /// port.
    /// </summary>
    public Task<IReadOnlyList<PipelineHistory>> ListHistoryAsync(
        Guid hospitalId,
        Guid pipelineId,
        CancellationToken cancellationToken);

    /// <summary>Adds a new pipeline (with its initial history) to the change tracker.</summary>
    public void Add(Pipeline pipeline);
}

/// <summary>
/// Filter criteria for the pipeline list endpoint. All fields are optional;
/// a fully empty criteria returns the hospital roster.
/// </summary>
public sealed record PipelineListCriteria(
    PipelineSubjectType? SubjectType,
    Guid? SubjectId,
    PipelineStatus? Status,
    Guid? PatientId = null,
    Guid? EncounterId = null);
