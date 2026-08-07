using Cynara.Domain.Tasks;

namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Projects task aggregates to their public DTO shapes.
/// </summary>
internal static class TaskMappers
{
    public static TaskDto ToDto(ClinicalTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskDto(
            task.Id,
            task.PipelineId,
            task.WorkflowVersionId,
            task.NodeId,
            task.Name,
            task.Description,
            TaskWorkflowHelpers.FormatStatus(task.Status),
            task.AssignedActor,
            task.AssignedRole,
            task.AssignedDiscipline,
            task.PatientId,
            task.EncounterId,
            task.FormCode,
            task.FormVersion,
            task.DueAt,
            task.ClaimedBy,
            task.ClaimedAt,
            task.CompletedBy,
            task.CompletedAt,
            task.CanceledBy,
            task.CanceledAt,
            task.RowVersion,
            task.CreatedAt,
            task.UpdatedAt);
    }
}
