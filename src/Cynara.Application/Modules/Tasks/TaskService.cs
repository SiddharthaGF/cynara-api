using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Tasks.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Tasks;

namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Default implementation of <see cref="ITaskService"/>. Reads are
/// hospital-scoped and capability-gated; transitions enforce optimistic
/// concurrency, run through the explicit task state machine, and emit audit
/// events in the mutation's unit-of-work boundary.
/// </summary>
public sealed class TaskService(
    ITaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    ICapabilityGuard capabilityGuard) : ITaskService
{
    /// <inheritdoc />
    public async Task<TaskDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.TasksRead, cancellationToken)
            .ConfigureAwait(false);
        ClinicalTask task = await RequireTaskAsync(
                id,
                track: false,
                cancellationToken)
            .ConfigureAwait(false);
        return TaskMappers.ToDto(task);
    }

    /// <inheritdoc />
    public async Task<TaskListResponse> ListAsync(
        TaskListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.TasksRead, cancellationToken)
            .ConfigureAwait(false);

        ClinicalTaskStatus? status = TaskWorkflowHelpers.ParseStatusOrNull(request.Status);
        IReadOnlyList<ClinicalTask> items = await tasks
            .ListAsync(
                hospitalContext.HospitalId,
                new TaskListCriteria(
                    status,
                    request.PatientId,
                    request.EncounterId,
                    request.PipelineId,
                    request.AssignedActor,
                    request.AssignedRole,
                    request.AssignedDiscipline),
                cancellationToken)
            .ConfigureAwait(false);
        return new TaskListResponse(
            [.. items.Select(TaskMappers.ToDto)]);
    }

    /// <inheritdoc />
    public async Task<TaskDto> ClaimAsync(
        Guid id,
        ClaimTaskRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.TasksWrite, cancellationToken)
            .ConfigureAwait(false);

        ClinicalTask task = await RequireTaskAsync(
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false);
        TaskWorkflowHelpers.EnsureConcurrency(task.RowVersion, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        ClinicalTaskLifecycle.Claim(task, actorId, now);
        task.RowVersion = request.RowVersion + 1;
        auditWriter.Append(
            AuditEntityTypes.Task,
            task.Id,
            "task.claimed",
            actorId,
            now,
            new
            {
                pipelineId = task.PipelineId,
                nodeId = task.NodeId,
            },
            patientId: task.PatientId,
            encounterId: task.EncounterId,
            workflowDefinitionId: task.WorkflowDefinitionId);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TaskMappers.ToDto(task);
    }

    /// <inheritdoc />
    public async Task<TaskDto> CompleteAsync(
        Guid id,
        TransitionTaskRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.TasksWrite, cancellationToken)
            .ConfigureAwait(false);

        ClinicalTask task = await RequireTaskAsync(
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false);
        TaskWorkflowHelpers.EnsureConcurrency(task.RowVersion, request.RowVersion);
        string reason = TaskWorkflowHelpers.EnsureReasonLength(request.Reason);

        DateTimeOffset now = timeProvider.GetUtcNow();
        ClinicalTaskLifecycle.Complete(task, actorId, now);
        task.RowVersion = request.RowVersion + 1;
        auditWriter.Append(
            AuditEntityTypes.Task,
            task.Id,
            "task.completed",
            actorId,
            now,
            new
            {
                reason,
                pipelineId = task.PipelineId,
                nodeId = task.NodeId,
            },
            patientId: task.PatientId,
            encounterId: task.EncounterId,
            workflowDefinitionId: task.WorkflowDefinitionId);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TaskMappers.ToDto(task);
    }

    /// <inheritdoc />
    public async Task<TaskDto> CancelAsync(
        Guid id,
        TransitionTaskRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.TasksWrite, cancellationToken)
            .ConfigureAwait(false);

        ClinicalTask task = await RequireTaskAsync(
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false);
        TaskWorkflowHelpers.EnsureConcurrency(task.RowVersion, request.RowVersion);
        string reason = TaskWorkflowHelpers.EnsureReasonLength(request.Reason);

        DateTimeOffset now = timeProvider.GetUtcNow();
        ClinicalTaskLifecycle.Cancel(task, actorId, now);
        task.RowVersion = request.RowVersion + 1;
        auditWriter.Append(
            AuditEntityTypes.Task,
            task.Id,
            "task.canceled",
            actorId,
            now,
            new
            {
                reason,
                pipelineId = task.PipelineId,
                nodeId = task.NodeId,
            },
            patientId: task.PatientId,
            encounterId: task.EncounterId,
            workflowDefinitionId: task.WorkflowDefinitionId);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TaskMappers.ToDto(task);
    }

    private async Task<ClinicalTask> RequireTaskAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        ClinicalTask? task = await tasks
            .FindByIdAsync(hospitalContext.HospitalId, id, track, cancellationToken)
            .ConfigureAwait(false);
        return task ?? throw new NotFoundException(
            $"Clinical task '{id}' was not found.");
    }
}
