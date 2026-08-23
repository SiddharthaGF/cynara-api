namespace Cynara.Application.Common;

/// <summary>
/// Shared review-gated version state machine for catalog definitions (form
/// and workflow): draft → review → published → retired. Illegal transitions
/// throw <see cref="InvalidStateException"/>. Component versions (no review
/// gate) intentionally use a different machine.
/// </summary>
internal static class ReviewableVersionLifecycle
{
    public enum Trigger
    {
        SubmitForReview = 0,
        WithdrawFromReview = 1,
        RejectReview = 2,
        Publish = 3,
        Retire = 4,
    }

    public static string FormatTrigger(Trigger trigger)
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

    public static bool IsAllowed<TStatus>(
        TStatus status,
        Trigger trigger,
        params (TStatus Status, Trigger Trigger)[] allowed)
        where TStatus : struct, Enum
    {
        foreach ((TStatus Status, Trigger Trigger) candidate in allowed)
        {
            if (candidate.Status.Equals(status) && candidate.Trigger == trigger)
            {
                return true;
            }
        }

        return false;
    }
}
