namespace Cynara.Domain.Tasks;

/// <summary>
/// Lifecycle status of a clinical task. Tasks start <see cref="Open"/>, may
/// transition to <see cref="Claimed"/> by an actor, and reach a terminal
/// <see cref="Completed"/> or <see cref="Canceled"/> state. Terminal states
/// are irreversible.
/// </summary>
public enum ClinicalTaskStatus
{
    Open = 0,
    Claimed = 1,
    Completed = 2,
    Canceled = 3,
}
