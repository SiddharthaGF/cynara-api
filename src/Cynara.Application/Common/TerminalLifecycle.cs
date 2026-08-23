namespace Cynara.Application.Common;

/// <summary>
/// Shared terminal-state machine for runtime aggregates (encounter,
/// pipeline, clinical document): complete, cancel, and enter-in-error are
/// terminal and irreversible. Each aggregate keeps a thin Fire facade with
/// its transition table; this class owns triggers, verbs, and the check.
/// </summary>
internal static class TerminalLifecycle
{
    public enum Trigger
    {
        Complete = 0,
        Cancel = 1,
        EnterInError = 2,
    }

    public static string FormatTrigger(Trigger trigger)
    {
        return trigger switch
        {
            Trigger.Complete => "complete",
            Trigger.Cancel => "cancel",
            Trigger.EnterInError => "enter in error",
            _ => trigger.ToString(),
        };
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
