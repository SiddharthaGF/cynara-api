using Cynara.Application;
using Cynara.Application.Modules.Invitations;
using Cynara.Domain.Invitations;

namespace Cynara.Api.Tests.Invitations.UnitTests;

/// <summary>
/// Unit coverage for the invitation lifecycle state machine: every legal
/// transition plus the illegal-transition contract (typed error, no
/// mutation).
/// </summary>
public sealed class InvitationLifecycleUnitTests
{
    [Fact]
    public void Fire_Accept_FromPending_TransitionsToAccepted()
    {
        Invitation invitation = PendingInvitation();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Accept);
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
    }

    [Fact]
    public void Fire_Expire_FromPending_TransitionsToExpired()
    {
        Invitation invitation = PendingInvitation();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Expire);
        Assert.Equal(InvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public void Fire_Revoke_FromPending_TransitionsToRevoked()
    {
        Invitation invitation = PendingInvitation();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Revoke);
        Assert.Equal(InvitationStatus.Revoked, invitation.Status);
    }

    [Fact]
    public void Fire_Cancel_FromPending_TransitionsToCancelled()
    {
        Invitation invitation = PendingInvitation();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Cancel);
        Assert.Equal(InvitationStatus.Cancelled, invitation.Status);
    }

    [Fact]
    public void Fire_Resend_FromExpired_RestartsValidityAsPending()
    {
        Invitation invitation = ExpiredInvitation();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Resend);
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public void Fire_Cancel_FromExpired_TransitionsToCancelled()
    {
        Invitation invitation = ExpiredInvitation();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Cancel);
        Assert.Equal(InvitationStatus.Cancelled, invitation.Status);
    }

    [Theory]
    [InlineData(InvitationStatus.Accepted)]
    [InlineData(InvitationStatus.AlreadyUsed)]
    [InlineData(InvitationStatus.Cancelled)]
    [InlineData(InvitationStatus.Revoked)]
    public void Fire_FromDeadEndState_AnyTrigger_ThrowsAndLeavesStateUntouched(
        InvitationStatus initialStatus)
    {
        Invitation invitation = PendingInvitation();
        invitation.Status = initialStatus;

        foreach (InvitationLifecycle.Trigger trigger
            in Enum.GetValues<InvitationLifecycle.Trigger>())
        {
            InvalidStateException exception =
                Assert.Throws<InvalidStateException>(
                    () => InvitationLifecycle.Fire(invitation, trigger));

            Assert.Contains(
                initialStatus.ToString(),
                exception.Message,
                StringComparison.Ordinal);
            Assert.Equal(initialStatus, invitation.Status);
        }
    }

    /// <summary>
    /// Expired invitations must be resent or cancelled first; direct
    /// acceptance is not a legal transition.
    /// </summary>
    [Fact]
    public void Fire_Accept_FromExpired_ThrowsAndLeavesStateUntouched()
    {
        Invitation invitation = ExpiredInvitation();

        InvalidStateException exception =
            Assert.Throws<InvalidStateException>(
                () => InvitationLifecycle.Fire(
                    invitation, InvitationLifecycle.Trigger.Accept));

        Assert.Contains(
            nameof(InvitationStatus.Expired),
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(InvitationStatus.Expired, invitation.Status);
    }

    private static Invitation PendingInvitation()
    {
        return new Invitation
        {
            Email = "invitee@cynara.dev",
            HospitalId = Guid.NewGuid(),
            TokenHash = new string('A', 64),
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(72),
        };
    }

    private static Invitation ExpiredInvitation()
    {
        return new Invitation
        {
            Email = "invitee@cynara.dev",
            HospitalId = Guid.NewGuid(),
            Status = InvitationStatus.Expired,
            TokenHash = new string('A', 64),
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-4),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-3),
        };
    }
}
