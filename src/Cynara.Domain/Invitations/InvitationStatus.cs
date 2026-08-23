namespace Cynara.Domain.Invitations;

/// <summary>
/// Lifecycle states of a member invitation. Pending is the only active
/// state; accepted, already-used, and cancelled are terminal. Transitions
/// are owned by the application-layer lifecycle.
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
