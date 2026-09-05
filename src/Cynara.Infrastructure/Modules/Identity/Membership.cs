using Cynara.Domain.Memberships;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Bridges an identity user to a Cynara actor within a hospital workspace;
/// period rows carry a lifecycle status so revoked history coexists with
/// the single active membership per (user, hospital) and actor.
/// </summary>
public sealed class Membership
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid HospitalId { get; set; }

    public string ActorId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public DateTimeOffset ActivatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint RowVersion { get; set; }
}
