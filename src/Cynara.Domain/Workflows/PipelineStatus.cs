namespace Cynara.Domain.Workflows;

/// <summary>
/// Lifecycle status of a workflow pipeline. Pipelines start <see cref="Running"/>,
/// auto-complete when a transition enters an end node, and can otherwise be
/// explicitly completed, canceled, or entered-in-error. Terminal states are
/// irreversible and remain historically queryable.
/// </summary>
public enum PipelineStatus
{
    Running = 0,
    Completed = 1,
    Canceled = 2,
    EnteredInError = 3,
}
