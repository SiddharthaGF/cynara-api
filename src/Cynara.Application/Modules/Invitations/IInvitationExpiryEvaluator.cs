using Cynara.Domain.Invitations;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Central lazy-expiry evaluation for invitations. The evaluator owns the
/// single expiry rule so the trigger strategy can evolve without contract
/// change; callers own audit staging and notification inside their unit of
/// work.
/// </summary>
public interface IInvitationExpiryEvaluator
{
    /// <summary>
    /// Marks a pending invitation past <see cref="Invitation.ExpiresAt"/> as
    /// expired via the lifecycle authority. Returns true when the invitation
    /// transitioned; repeated evaluation is idempotent.
    /// </summary>
    public Task<bool> EvaluateAsync(
        Invitation invitation,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
