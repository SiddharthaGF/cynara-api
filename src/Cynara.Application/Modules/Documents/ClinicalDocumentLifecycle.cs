using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Switch-based clinical document lifecycle. Completed content is immutable;
/// terminal states are irreversible. Invalid transitions throw
/// <see cref="InvalidStateException"/> without mutating the entity so the
/// unit of work can roll back cleanly and the bound response stays intact.
/// </summary>
internal static class ClinicalDocumentLifecycle
{
    public enum Trigger
    {
        Complete = 0,
        Cancel = 1,
        EnterInError = 2,
    }

    public static void Fire(ClinicalDocument document, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(document);
        bool valid = (document.Status, trigger) switch
        {
            (ClinicalDocumentStatus.InProgress, Trigger.Complete) => true,
            (ClinicalDocumentStatus.InProgress, Trigger.Cancel) => true,
            (ClinicalDocumentStatus.InProgress, Trigger.EnterInError) => true,
            (ClinicalDocumentStatus.Completed, Trigger.EnterInError) => true,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} a clinical document in "
                + "status '"
                + ClinicalDocumentWorkflowHelpers.FormatStatus(document.Status)
                + "'.");
        }

        document.Status = trigger switch
        {
            Trigger.Complete => ClinicalDocumentStatus.Completed,
            Trigger.Cancel => ClinicalDocumentStatus.Canceled,
            Trigger.EnterInError => ClinicalDocumentStatus.EnteredInError,
            _ => document.Status,
        };
    }

    private static string FormatTrigger(Trigger trigger)
    {
        return trigger switch
        {
            Trigger.Complete => "complete",
            Trigger.Cancel => "cancel",
            Trigger.EnterInError => "enter in error",
            _ => trigger.ToString(),
        };
    }
}
