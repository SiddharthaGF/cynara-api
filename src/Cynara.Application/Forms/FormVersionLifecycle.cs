using Cynara.Application.Common;

using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

internal static class FormVersionLifecycle
{
    public static void Fire(
        FormVersion version,
        ReviewableVersionLifecycle.Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(version);
        bool valid = ReviewableVersionLifecycle.IsAllowed(
            version.Status,
            trigger,
            (FormVersionStatus.Draft, ReviewableVersionLifecycle.Trigger.SubmitForReview),
            (FormVersionStatus.Review, ReviewableVersionLifecycle.Trigger.WithdrawFromReview),
            (FormVersionStatus.Review, ReviewableVersionLifecycle.Trigger.RejectReview),
            (FormVersionStatus.Review, ReviewableVersionLifecycle.Trigger.Publish),
            (FormVersionStatus.Published, ReviewableVersionLifecycle.Trigger.Retire));
        if (!valid)
        {
            string verb = ReviewableVersionLifecycle.FormatTrigger(trigger);
            throw new InvalidStateException(
                $"Cannot {verb} a form version in status '{version.Status}'.");
        }

        version.Status = (version.Status, trigger) switch
        {
            (FormVersionStatus.Draft, ReviewableVersionLifecycle.Trigger.SubmitForReview) => FormVersionStatus.Review,
            (FormVersionStatus.Review, ReviewableVersionLifecycle.Trigger.WithdrawFromReview) => FormVersionStatus.Draft,
            (FormVersionStatus.Review, ReviewableVersionLifecycle.Trigger.RejectReview) => FormVersionStatus.Draft,
            (FormVersionStatus.Review, ReviewableVersionLifecycle.Trigger.Publish) => FormVersionStatus.Published,
            (FormVersionStatus.Published, ReviewableVersionLifecycle.Trigger.Retire) => FormVersionStatus.Retired,
            _ => version.Status,
        };
    }

    public static IReadOnlyList<string> DescribePermittedTransitions()
    {
        return ReviewableVersionLifecycle.DescribePermittedTransitions();
    }
}
