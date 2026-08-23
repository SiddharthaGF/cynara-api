using Cynara.Domain.Invitations;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Central lazy-expiry evaluation for invitations. Callers (admin queries,
/// acceptance resolution, and a future proactive hosted detector) pass the
/// current instant; the evaluator owns the single expiry rule so the trigger
/// strategy can evolve without contract change. State mutation happens here;
/// callers own audit staging and notification inside their unit of work.
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
