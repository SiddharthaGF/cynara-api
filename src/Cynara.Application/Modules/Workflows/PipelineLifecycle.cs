using Cynara.Application.Common;

using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Pipeline lifecycle facade over the shared terminal state machine: only
/// running pipelines can be completed, canceled, or entered-in-error;
/// terminal states are irreversible. Invalid transitions throw without
/// mutating the entity. End-node completion belongs to the advance workflow.
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
