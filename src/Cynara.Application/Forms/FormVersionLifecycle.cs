using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

internal static class FormVersionLifecycle
{
    public enum Trigger
    {
        SubmitForReview = 0,
        WithdrawFromReview = 1,
        RejectReview = 2,
        Publish = 3,
        Retire = 4,
    }

    public static void Fire(FormVersion version, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(version);
        bool valid = (version.Status, trigger) switch
        {
            (FormVersionStatus.Draft, Trigger.SubmitForReview) => true,
            (FormVersionStatus.Review, Trigger.WithdrawFromReview) => true,
            (FormVersionStatus.Review, Trigger.RejectReview) => true,
            (FormVersionStatus.Review, Trigger.Publish) => true,
            (FormVersionStatus.Published, Trigger.Retire) => true,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} a form version in status '{version.Status}'.");
        }

        version.Status = (version.Status, trigger) switch
        {
            (FormVersionStatus.Draft, Trigger.SubmitForReview) => FormVersionStatus.Review,
            (FormVersionStatus.Review, Trigger.WithdrawFromReview) => FormVersionStatus.Draft,
            (FormVersionStatus.Review, Trigger.RejectReview) => FormVersionStatus.Draft,
            (FormVersionStatus.Review, Trigger.Publish) => FormVersionStatus.Published,
            (FormVersionStatus.Published, Trigger.Retire) => FormVersionStatus.Retired,
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
