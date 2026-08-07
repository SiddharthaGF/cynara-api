using Cynara.Domain.Tasks;

namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Explicit lifecycle for the clinical task aggregate: open tasks can be
/// claimed, completed, or canceled; claimed tasks can be completed or
/// canceled; terminal states are irreversible. Invalid transitions throw
/// <see cref="InvalidStateException"/> without mutating the entity so the
/// unit of work can roll back cleanly. Documents and pipeline hooks use the
/// same transitions as the task API.
/// </summary>
internal static class ClinicalTaskLifecycle
{
    public static void Claim(
        ClinicalTask task,
        string? actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        RequireActive(task, "claim");
        if (task.Status != ClinicalTaskStatus.Open)
        {
            throw new InvalidStateException(
                "Only an open task can be claimed; task is '"
                + TaskWorkflowHelpers.FormatStatus(task.Status)
                + "'.");
        }

        task.Status = ClinicalTaskStatus.Claimed;
        task.ClaimedBy = actorId;
        task.ClaimedAt = now;
        task.UpdatedAt = now;
    }

    public static void Complete(
        ClinicalTask task,
        string? actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        RequireActive(task, "complete");
        task.Status = ClinicalTaskStatus.Completed;
        task.CompletedBy = actorId;
        task.CompletedAt = now;
        task.UpdatedAt = now;
    }

    public static void Cancel(
        ClinicalTask task,
        string? actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        RequireActive(task, "cancel");
        task.Status = ClinicalTaskStatus.Canceled;
        task.CanceledBy = actorId;
        task.CanceledAt = now;
        task.UpdatedAt = now;
    }

    private static void RequireActive(ClinicalTask task, string action)
    {
        if (task.Status is ClinicalTaskStatus.Completed
            or ClinicalTaskStatus.Canceled)
        {
            throw new InvalidStateException(
                $"Cannot {action} a task in status '"
                + TaskWorkflowHelpers.FormatStatus(task.Status)
                + "'.");
        }
    }
}
