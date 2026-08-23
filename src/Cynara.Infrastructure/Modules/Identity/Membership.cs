namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Bridges an identity user to a Cynara actor within a hospital workspace;
/// the unique (UserId, HospitalId) pair plus an active hospital forms the
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
