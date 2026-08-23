namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Contract for the workflow pipeline runtime: start pins the exact
/// published version, advance evaluates conditions server-side and moves
/// the cursor, lifecycle operations complete/cancel/enter-in-error, and
/// every transition appends to the immutable progression history.
/// </summary>
public interface IPipelineService
{
    /// <summary>
    /// Starts a new pipeline for the supplied subject on a published workflow
    /// version (the requested semver, or the latest published when omitted).
    /// The version is pinned for the pipeline lifetime.
    /// </summary>
    public Task<PipelineDto> StartAsync(
        StartPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Returns one pipeline in the resolved hospital workspace.</summary>
    public Task<PipelineDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Lists pipelines in the resolved hospital workspace.</summary>
    public Task<PipelineListResponse> ListAsync(
        PipelineListRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the append-only progression history for one pipeline; an
    /// unknown or cross-tenant id is a 404, not an empty history.
    /// </summary>
    public Task<PipelineHistoryResponse> ListHistoryAsync(
        Guid pipelineId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the full pipeline journey for a patient record: every
    /// pipeline bound to the patient (directly or through its encounters),
    /// rendered from the pinned published version with its history.
    /// Soft-deleted patients remain queryable for historical rendering.
    /// </summary>
    public Task<PatientJourneyResponse> GetPatientJourneyAsync(
        Guid patientId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the full pipeline journey for an encounter: every pipeline
    /// bound to the encounter, rendered from the exact published workflow
    /// version at start time with the immutable progression history.
    /// </summary>
    public Task<EncounterJourneyResponse> GetEncounterJourneyAsync(
        Guid encounterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Advances a running pipeline one step. The server picks the outgoing
    /// transition (evaluating decision conditions against
    /// <paramref name="request"/> inputs); clients cannot choose the next
    /// node directly. Reaching an end node completes the pipeline.
    /// </summary>
    public Task<PipelineDto> AdvanceAsync(
        Guid id,
        AdvancePipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Completes a running pipeline.</summary>
    public Task<PipelineDto> CompleteAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Cancels a running pipeline.</summary>
    public Task<PipelineDto> CancelAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Enters a running pipeline in error.</summary>
    public Task<PipelineDto> EnterInErrorAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
