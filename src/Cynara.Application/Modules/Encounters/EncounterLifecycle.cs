using Cynara.Domain.Encounters;

namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Encounter lifecycle facade over the shared terminal state machine.
/// Terminal states are irreversible; invalid transitions throw
/// <see cref="InvalidStateException"/> without mutating the entity so the
/// unit of work can roll back cleanly.
/// </summary>
internal static class EncounterLifecycle
{
    public static void Fire(
        Encounter encounter,
        TerminalLifecycle.Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        bool valid = TerminalLifecycle.IsAllowed(
            encounter.Status,
            trigger,
            (EncounterStatus.Open, TerminalLifecycle.Trigger.Complete),
            (EncounterStatus.Open, TerminalLifecycle.Trigger.Cancel),
            (EncounterStatus.Open, TerminalLifecycle.Trigger.EnterInError));
        if (!valid)
        {
            string verb = TerminalLifecycle.FormatTrigger(trigger);
            throw new InvalidStateException(
                $"Cannot {verb} an encounter in status "
                + $"'{EncounterWorkflowHelpers.FormatStatus(encounter.Status)}'.");
        }

        encounter.Status = trigger switch
        {
            TerminalLifecycle.Trigger.Complete => EncounterStatus.Completed,
            TerminalLifecycle.Trigger.Cancel => EncounterStatus.Canceled,
            TerminalLifecycle.Trigger.EnterInError => EncounterStatus.EnteredInError,
            _ => encounter.Status,
        };
    }
}
