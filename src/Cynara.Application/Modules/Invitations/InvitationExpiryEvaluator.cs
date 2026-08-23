using Cynara.Domain.Invitations;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Default lazy-expiry evaluator. Pure decision logic: a pending invitation
/// whose <see cref="Invitation.ExpiresAt"/> has passed transitions to
/// expired through the lifecycle authority; everything else is untouched.
/// </summary>
public sealed class InvitationExpiryEvaluator : IInvitationExpiryEvaluator
{
    public Task<bool> EvaluateAsync(
        Invitation invitation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        _ = cancellationToken;
        if (invitation.Status != InvitationStatus.Pending
            || invitation.ExpiresAt > now)
        {
            return Task.FromResult(false);
        }

        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Expire);
        return Task.FromResult(true);
    }
}
