using Cynara.Domain.Invitations;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Explicit state machine for the invitation lifecycle:
/// pending → accepted/expired/revoked/cancelled, expired → cancelled, and
/// resend restarting validity from pending or expired. Illegal transitions
/// throw <see cref="InvalidStateException"/> rather than silently no-oping,
/// leaving the entity untouched so the unit of work rolls back cleanly and
/// no audit event is staged.
/// </summary>
internal static class InvitationLifecycle
{
    internal enum Trigger
    {
        Accept = 0,
        Expire = 1,
        Revoke = 2,
        Cancel = 3,
        Resend = 4,
    }

    public static void Fire(
        Invitation invitation,
        Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        bool valid = IsAllowed(
            invitation.Status,
            trigger,
            (InvitationStatus.Pending, Trigger.Accept),
            (InvitationStatus.Pending, Trigger.Expire),
            (InvitationStatus.Pending, Trigger.Revoke),
            (InvitationStatus.Pending, Trigger.Cancel),
            (InvitationStatus.Pending, Trigger.Resend),
            (InvitationStatus.Expired, Trigger.Cancel),
            (InvitationStatus.Expired, Trigger.Resend));
        if (!valid)
        {
            throw new InvalidStateException(
                $"Cannot {FormatTrigger(trigger)} an invitation in "
                + $"status '{invitation.Status}'.");
        }

        switch (trigger)
        {
            case Trigger.Accept:
                invitation.Status = InvitationStatus.Accepted;
                break;
            case Trigger.Expire:
                invitation.Status = InvitationStatus.Expired;
                break;
            case Trigger.Revoke:
                invitation.Status = InvitationStatus.Revoked;
                break;
            case Trigger.Cancel:
                invitation.Status = InvitationStatus.Cancelled;
                break;
            case Trigger.Resend:
                // Resend restarts the 72h window on the same row: status
                // stays/becomes Pending while the caller bumps LinkVersion
                // and re-stamps IssuedAt/ExpiresAt.
                invitation.Status = InvitationStatus.Pending;
                break;
            default:
                break;
        }
    }

    private static bool IsAllowed(
        InvitationStatus status,
        Trigger trigger,
        params (InvitationStatus Status, Trigger Trigger)[] allowed)
    {
        foreach ((InvitationStatus Status, Trigger Trigger) candidate in allowed)
        {
            if (candidate.Status == status && candidate.Trigger == trigger)
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatTrigger(Trigger trigger)
    {
        return trigger switch
        {
            Trigger.Accept => "accept",
            Trigger.Expire => "expire",
            Trigger.Revoke => "revoke",
            Trigger.Cancel => "cancel",
            Trigger.Resend => "resend",
            _ => trigger.ToString(),
        };
    }
}
