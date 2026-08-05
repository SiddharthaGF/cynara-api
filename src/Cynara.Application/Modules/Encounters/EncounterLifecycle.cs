using Cynara.Domain.Encounters;

namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Switch-based encounter lifecycle. Terminal states are irreversible;
/// invalid transitions throw <see cref="InvalidStateException"/> without
/// mutating the entity so the unit of work can roll back cleanly.
/// </summary>
internal static class EncounterLifecycle
{
    public enum Trigger
    {
        Complete = 0,
        Cancel = 1,
        EnterInError = 2,
    }

    public static void Fire(Encounter encounter, Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        bool valid = (encounter.Status, trigger) switch
        {
            (EncounterStatus.Open, Trigger.Complete) => true,
            (EncounterStatus.Open, Trigger.Cancel) => true,
            (EncounterStatus.Open, Trigger.EnterInError) => true,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} an encounter in status "
                + $"'{EncounterWorkflowHelpers.FormatStatus(encounter.Status)}'.");
        }

        encounter.Status = trigger switch
        {
            Trigger.Complete => EncounterStatus.Completed,
            Trigger.Cancel => EncounterStatus.Canceled,
            Trigger.EnterInError => EncounterStatus.EnteredInError,
            _ => encounter.Status,
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
