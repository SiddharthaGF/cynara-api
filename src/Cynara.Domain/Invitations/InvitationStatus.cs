namespace Cynara.Domain.Invitations;

/// <summary>
/// Lifecycle states of a member invitation. <see cref="Pending"/> is the
/// only active state; <see cref="Accepted"/>, <see cref="AlreadyUsed"/>,
/// and <see cref="Cancelled"/> are terminal. Transitions are owned by the
/// application-layer invitation lifecycle authority.
/// </summary>
public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Expired = 2,
    Revoked = 3,
    AlreadyUsed = 4,
    Cancelled = 5,
}
