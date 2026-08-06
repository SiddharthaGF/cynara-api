using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Explicit state machine for the workflow-version lifecycle:
/// draft → review → published → retired. Illegal transitions throw
/// <see cref="InvalidStateException"/> rather than silently no-oping.
/// </summary>
internal static class WorkflowVersionLifecycle
{
    public enum Trigger
    {
        SubmitForReview = 0,
        WithdrawFromReview = 1,
        RejectReview = 2,
        Publish = 3,
        Retire = 4,
    }

    public static void Fire(WorkflowVersion version, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(version);
        bool valid = (version.Status, trigger) switch
        {
            (WorkflowVersionStatus.Draft, Trigger.SubmitForReview) => true,
            (WorkflowVersionStatus.Review, Trigger.WithdrawFromReview) => true,
            (WorkflowVersionStatus.Review, Trigger.RejectReview) => true,
            (WorkflowVersionStatus.Review, Trigger.Publish) => true,
            (WorkflowVersionStatus.Published, Trigger.Retire) => true,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} a workflow version in status '{version.Status}'.");
        }

        version.Status = (version.Status, trigger) switch
        {
            (WorkflowVersionStatus.Draft, Trigger.SubmitForReview) => WorkflowVersionStatus.Review,
            (WorkflowVersionStatus.Review, Trigger.WithdrawFromReview) => WorkflowVersionStatus.Draft,
            (WorkflowVersionStatus.Review, Trigger.RejectReview) => WorkflowVersionStatus.Draft,
            (WorkflowVersionStatus.Review, Trigger.Publish) => WorkflowVersionStatus.Published,
            (WorkflowVersionStatus.Published, Trigger.Retire) => WorkflowVersionStatus.Retired,
            _ => version.Status,
        };
    }

    public static IReadOnlyList<string> DescribePermittedTransitions()
    {
        return
        [
            "Draft --SubmitForReview--> Review",
            "Review --WithdrawFromReview--> Draft",
            "Review --RejectReview--> Draft",
            "Review --Publish--> Published",
            "Published --Retire--> Retired",
        ];
    }

    private static string FormatTrigger(Trigger trigger)
    {
        return trigger switch
        {
            Trigger.SubmitForReview => "submit for review",
            Trigger.WithdrawFromReview => "withdraw from review",
            Trigger.RejectReview => "reject",
            Trigger.Publish => "publish",
            Trigger.Retire => "retire",
            _ => trigger.ToString(),
        };
    }
}
