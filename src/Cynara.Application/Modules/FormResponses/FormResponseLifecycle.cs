using Cynara.Domain.Forms;

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
        if (response.Status != FormResponseStatus.Draft
            || trigger != Trigger.Complete)
        {
            throw new InvalidStateException(
                $"Cannot complete a form response in status '{response.Status}'.");
        }

        response.Status = FormResponseStatus.Completed;
    }
}
