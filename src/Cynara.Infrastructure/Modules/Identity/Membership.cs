namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Bridges an identity user to a Cynara actor within a hospital workspace.
/// A user may hold one membership per hospital; the unique
/// <c>(UserId, HospitalId)</c> pair enforces that rule at the database
/// boundary. Presence of a membership plus an active hospital is the
/// active-membership rule used by actor resolution.
/// </summary>
public sealed class Membership
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid HospitalId { get; set; }

    public string ActorId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
