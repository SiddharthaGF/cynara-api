namespace Cynara.Domain.Workflows;

/// <summary>
/// Lifecycle status of a workflow pipeline. Pipelines start running,
/// auto-complete on entering an end node, or are completed, canceled, or
/// entered-in-error explicitly. Terminal states are irreversible.
/// </summary>
public enum PipelineStatus
{
    Running = 0,
    Completed = 1,
    Canceled = 2,
    EnteredInError = 3,
}
