using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Explicit lifecycle for the pipeline aggregate: only running pipelines can
/// be completed, canceled, or entered-in-error. Terminal states are
/// irreversible; invalid transitions throw <see cref="InvalidStateException"/>
/// without mutating the entity so the unit of work can roll back cleanly.
/// Reaching an end node is completed by the advance workflow, not here.
/// </summary>
internal static class PipelineLifecycle
{
    public enum Trigger
    {
        Complete = 0,
        Cancel = 1,
        EnterInError = 2,
    }

    public static void Fire(Pipeline pipeline, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        bool valid = (pipeline.Status, trigger) switch
        {
            (PipelineStatus.Running, Trigger.Complete) => true,
            (PipelineStatus.Running, Trigger.Cancel) => true,
            (PipelineStatus.Running, Trigger.EnterInError) => true,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} a pipeline in status "
                + $"'{PipelineWorkflowHelpers.FormatStatus(pipeline.Status)}'.");
        }

        pipeline.Status = trigger switch
        {
            Trigger.Complete => PipelineStatus.Completed,
            Trigger.Cancel => PipelineStatus.Canceled,
            Trigger.EnterInError => PipelineStatus.EnteredInError,
            _ => pipeline.Status,
        };
    }

    private static string FormatTrigger(Trigger trigger)
    {
        return trigger switch
        {
            Trigger.Complete => "complete",
            Trigger.Cancel => "cancel",
            Trigger.EnterInError => "enter in error",
            _ => trigger.ToString(),
        };
    }
}
