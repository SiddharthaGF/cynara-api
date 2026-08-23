using Cynara.Application.Common;

using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Clinical document lifecycle facade over the shared terminal state
/// machine. Completed content is immutable; terminal states are
/// irreversible. Invalid transitions throw without mutating the entity so
/// the unit of work rolls back and the bound response stays intact.
/// </summary>
internal static class ClinicalDocumentLifecycle
{
    public static void Fire(
        ClinicalDocument document,
        TerminalLifecycle.Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(document);
        bool valid = TerminalLifecycle.IsAllowed(
            document.Status,
            trigger,
            (ClinicalDocumentStatus.InProgress, TerminalLifecycle.Trigger.Complete),
            (ClinicalDocumentStatus.InProgress, TerminalLifecycle.Trigger.Cancel),
            (ClinicalDocumentStatus.InProgress, TerminalLifecycle.Trigger.EnterInError),
            (ClinicalDocumentStatus.Completed, TerminalLifecycle.Trigger.EnterInError));
        if (!valid)
        {
            string verb = TerminalLifecycle.FormatTrigger(trigger);
            throw new InvalidStateException(
                $"Cannot {verb} a clinical document in "
                + "status '"
                + ClinicalDocumentWorkflowHelpers.FormatStatus(document.Status)
                + "'.");
        }

        document.Status = trigger switch
        {
            TerminalLifecycle.Trigger.Complete => ClinicalDocumentStatus.Completed,
            TerminalLifecycle.Trigger.Cancel => ClinicalDocumentStatus.Canceled,
            TerminalLifecycle.Trigger.EnterInError => ClinicalDocumentStatus.EnteredInError,
            _ => document.Status,
        };
    }
}
