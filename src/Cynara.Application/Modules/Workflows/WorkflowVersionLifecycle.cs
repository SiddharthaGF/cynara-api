using Cynara.Application.Common;

using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Explicit state machine for the workflow-version lifecycle:
/// draft → review → published → retired. Illegal transitions throw
/// <see cref="InvalidStateException"/> rather than silently no-oping.
/// </summary>
internal static class WorkflowVersionLifecycle
{
    public static void Fire(
        WorkflowVersion version,
        ReviewableVersionLifecycle.Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(version);
        bool valid = ReviewableVersionLifecycle.IsAllowed(
            version.Status,
            trigger,
            (WorkflowVersionStatus.Draft, ReviewableVersionLifecycle.Trigger.SubmitForReview),
            (WorkflowVersionStatus.Review, ReviewableVersionLifecycle.Trigger.WithdrawFromReview),
            (WorkflowVersionStatus.Review, ReviewableVersionLifecycle.Trigger.RejectReview),
            (WorkflowVersionStatus.Review, ReviewableVersionLifecycle.Trigger.Publish),
            (WorkflowVersionStatus.Published, ReviewableVersionLifecycle.Trigger.Retire));
        if (!valid)
        {
            string verb = ReviewableVersionLifecycle.FormatTrigger(trigger);
            throw new InvalidStateException(
                $"Cannot {verb} a workflow version in status '{version.Status}'.");
        }

        version.Status = (version.Status, trigger) switch
        {
            (WorkflowVersionStatus.Draft, ReviewableVersionLifecycle.Trigger.SubmitForReview) => WorkflowVersionStatus.Review,
            (WorkflowVersionStatus.Review, ReviewableVersionLifecycle.Trigger.WithdrawFromReview) => WorkflowVersionStatus.Draft,
            (WorkflowVersionStatus.Review, ReviewableVersionLifecycle.Trigger.RejectReview) => WorkflowVersionStatus.Draft,
            (WorkflowVersionStatus.Review, ReviewableVersionLifecycle.Trigger.Publish) => WorkflowVersionStatus.Published,
            (WorkflowVersionStatus.Published, ReviewableVersionLifecycle.Trigger.Retire) => WorkflowVersionStatus.Retired,
            _ => version.Status,
        };
    }
}
