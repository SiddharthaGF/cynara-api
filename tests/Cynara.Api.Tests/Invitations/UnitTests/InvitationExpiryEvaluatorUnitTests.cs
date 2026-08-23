using Cynara.Application.Modules.Invitations;
using Cynara.Domain.Invitations;

namespace Cynara.Api.Tests.Invitations.UnitTests;

/// <summary>
/// Unit coverage for the lazy-expiry evaluator: the single expiry rule that
/// admin queries, acceptance resolution, and any future proactive detector
/// share.
/// </summary>
public sealed class InvitationExpiryEvaluatorUnitTests
{
    private readonly InvitationExpiryEvaluator evaluator = new();

    [Fact]
    public async Task Evaluate_BeforeExpiry_ReturnsFalseAndKeepsPending()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Invitation invitation = PendingInvitation(now.AddHours(1));

        bool changed = await evaluator.EvaluateAsync(
            invitation,
            now,
            CancellationToken.None);

        Assert.False(changed);
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public async Task Evaluate_AtExactDeadline_Expires()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow;
        Invitation invitation = PendingInvitation(deadline);

        bool changed = await evaluator.EvaluateAsync(
            invitation,
            deadline,
            CancellationToken.None);

        Assert.True(changed);
        Assert.Equal(InvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public async Task Evaluate_PastExpiry_TransitionsToExpired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Invitation invitation = PendingInvitation(now.AddDays(-1));

        bool changed = await evaluator.EvaluateAsync(
            invitation,
            now,
            CancellationToken.None);

        Assert.True(changed);
        Assert.Equal(InvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public async Task Evaluate_AlreadyExpired_IsIdempotent()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Invitation invitation = PendingInvitation(now.AddDays(-1));
        _ = await evaluator.EvaluateAsync(
            invitation,
            now,
            CancellationToken.None);

        bool secondPass = await evaluator.EvaluateAsync(
            invitation,
            now,
            CancellationToken.None);

        Assert.False(secondPass);
        Assert.Equal(InvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public async Task Evaluate_NonPendingPastDeadline_IsIgnored()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Invitation accepted = PendingInvitation(now.AddDays(-2));
        accepted.Status = InvitationStatus.Accepted;

        bool changed = await evaluator.EvaluateAsync(
            accepted,
            now,
            CancellationToken.None);

        Assert.False(changed);
        Assert.Equal(InvitationStatus.Accepted, accepted.Status);
    }

    private static Invitation PendingInvitation(DateTimeOffset expiresAt)
    {
        return new Invitation
        {
            Email = "invitee@cynara.dev",
            HospitalId = Guid.NewGuid(),
            TokenHash = new string('B', 64),
            IssuedAt = expiresAt.AddHours(-72),
            ExpiresAt = expiresAt,
        };
    }
}
