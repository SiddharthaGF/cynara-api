using Cynara.Application.Common;

using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Pipeline lifecycle facade over the shared terminal state machine: only
/// running pipelines can be completed, canceled, or entered-in-error.
/// Terminal states are irreversible; invalid transitions throw
/// <see cref="InvalidStateException"/> without mutating the entity so the
/// unit of work can roll back cleanly. Reaching an end node is completed by
/// the advance workflow, not here.
/// </summary>
internal static class PipelineLifecycle
{
    public static void Fire(
        Pipeline pipeline,
        TerminalLifecycle.Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        bool valid = TerminalLifecycle.IsAllowed(
            pipeline.Status,
            trigger,
            (PipelineStatus.Running, TerminalLifecycle.Trigger.Complete),
            (PipelineStatus.Running, TerminalLifecycle.Trigger.Cancel),
            (PipelineStatus.Running, TerminalLifecycle.Trigger.EnterInError));
        if (!valid)
        {
            string verb = TerminalLifecycle.FormatTrigger(trigger);
            throw new InvalidStateException(
                $"Cannot {verb} a pipeline in status "
                + $"'{PipelineWorkflowHelpers.FormatStatus(pipeline.Status)}'.");
        }

        pipeline.Status = trigger switch
        {
            TerminalLifecycle.Trigger.Complete => PipelineStatus.Completed,
            TerminalLifecycle.Trigger.Cancel => PipelineStatus.Canceled,
            TerminalLifecycle.Trigger.EnterInError => PipelineStatus.EnteredInError,
            _ => pipeline.Status,
        };
    }
}
