using Cynara.Domain.Forms;

using Stateless;

namespace Cynara.Application.Modules.FormResponses;

internal static class FormResponseLifecycle
{
    public enum Trigger
    {
        Complete = 0,
    }

    public static void Fire(FormResponse response, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(response);
        StateMachine<FormResponseStatus, Trigger> machine = Create(response);
        if (!machine.CanFire(trigger))
        {
            throw new InvalidStateException(
                $"Cannot complete a form response in status '{response.Status}'.");
        }

        machine.Fire(trigger);
    }

    private static StateMachine<FormResponseStatus, Trigger> Create(
        FormResponse response)
    {
        var machine = new StateMachine<FormResponseStatus, Trigger>(
            () => response.Status,
            status => response.Status = status);

        _ = machine.Configure(FormResponseStatus.Draft)
            .Permit(Trigger.Complete, FormResponseStatus.Completed);

        _ = machine.Configure(FormResponseStatus.Completed);

        return machine;
    }
}
