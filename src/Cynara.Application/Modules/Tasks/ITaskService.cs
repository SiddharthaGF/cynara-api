namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Contract for the clinical task lifecycle: reads are hospital-scoped,
/// transitions require optimistic concurrency, and claim/complete/cancel
/// transitions emit audit events in the same unit-of-work boundary.
/// </summary>
public interface ITaskService
{
    /// <summary>Returns one task in the resolved hospital workspace.</summary>
    public Task<TaskDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Lists tasks in the resolved hospital workspace.</summary>
    public Task<TaskListResponse> ListAsync(
        TaskListRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims an open task for the supplied actor. Only open tasks can be
    /// claimed.
    /// </summary>
    public Task<TaskDto> ClaimAsync(
        Guid id,
        ClaimTaskRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Completes an open or claimed task. Terminal states are irreversible.
    /// </summary>
    public Task<TaskDto> CompleteAsync(
        Guid id,
        TransitionTaskRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cancels an open or claimed task. Terminal states are irreversible.
    /// </summary>
    public Task<TaskDto> CancelAsync(
        Guid id,
        TransitionTaskRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
