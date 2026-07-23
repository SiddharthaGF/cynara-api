using Cynara.Domain.Forms;

using Stateless;

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
        StateMachine<FormVersionStatus, Trigger> machine = Create(version);
        if (!machine.CanFire(trigger))
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} a form version in status '{version.Status}'.");
        }

        machine.Fire(trigger);
    }

    public static IReadOnlyList<string> DescribePermittedTransitions()
    {
        // Keep in sync with Create(...).Configure(...) below.
        return
        [
            "Draft --SubmitForReview--> Review",
            "Review --WithdrawFromReview--> Draft",
            "Review --RejectReview--> Draft",
            "Review --Publish--> Published",
            "Published --Retire--> Retired",
        ];
    }

    private static StateMachine<FormVersionStatus, Trigger> Create(
        FormVersion version)
    {
        var machine = new StateMachine<FormVersionStatus, Trigger>(
            () => version.Status,
            status => version.Status = status);

        _ = machine.Configure(FormVersionStatus.Draft)
            .Permit(Trigger.SubmitForReview, FormVersionStatus.Review);

        _ = machine.Configure(FormVersionStatus.Review)
            .Permit(Trigger.WithdrawFromReview, FormVersionStatus.Draft)
            .Permit(Trigger.RejectReview, FormVersionStatus.Draft)
            .Permit(Trigger.Publish, FormVersionStatus.Published);

        _ = machine.Configure(FormVersionStatus.Published)
            .Permit(Trigger.Retire, FormVersionStatus.Retired);

        _ = machine.Configure(FormVersionStatus.Retired);

        return machine;
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
