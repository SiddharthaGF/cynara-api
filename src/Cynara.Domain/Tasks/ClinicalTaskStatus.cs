namespace Cynara.Domain.Tasks;

/// <summary>
/// Lifecycle status of a clinical task: open → claimed → terminal
/// completed or canceled. Terminal states are irreversible.
/// </summary>
public enum ClinicalTaskStatus
{
    Open = 0,
    Claimed = 1,
    Completed = 2,
    Canceled = 3,
}
