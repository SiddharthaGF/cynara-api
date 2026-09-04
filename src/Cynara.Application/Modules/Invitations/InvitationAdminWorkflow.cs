using System.Buffers.Text;
using System.Security.Cryptography;

using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Invitations.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Invitations;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Notification port for invitation events. Implementations are transport
/// sinks (Development logging today); they never receive token material.
/// </summary>
public interface IInvitationNotifier
{
    public Task InvitationExpiredAsync(
        InvitationExpiryNotice notice,
        IReadOnlyList<string> recipientActorIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Wire view of an invitation: lifecycle metadata only — never link token
/// or hash material.
/// </summary>
public sealed record InvitationView(
    Guid Id,
    string Email,
    Guid HospitalId,
    string Status,
    int LinkVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Creation or resend outcome. The raw token appears exactly once, here,
/// and is never persisted, logged, or audited.
/// </summary>
public sealed record CreateInvitationResult(InvitationView Invitation, string Token);

public sealed record CreateInvitationRequest(string Email, string? ProfileSnapshot);

/// <summary>Token-free expiry announcement payload.</summary>
public sealed record InvitationExpiryNotice(
    Guid InvitationId,
    string Email,
    Guid HospitalId,
    int LinkVersion);

/// <summary>
/// Administrative invitation lifecycle workflows. Every mutation loads
/// tracked state, stages audit through <see cref="IAuditWriter"/>, and
/// commits state plus audit with exactly one <c>SaveChangesAsync</c>. Lazy
/// expiry runs before each transition decision; expiry notifications fire
/// only after the owning commit succeeds.
/// </summary>
public sealed class InvitationAdminWorkflow(
    IInvitationRepository invitations,
    IInvitationExpiryEvaluator expiryEvaluator,
    IInvitationNotifier notifier,
    ICapabilityGuard capabilityGuard,
    IProfileSnapshotValidator profileSnapshotValidator,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork)
{
    private static readonly TimeSpan LinkValidity = TimeSpan.FromHours(72);

    /// <summary>Issues a pending invitation with a fresh 72-hour link.</summary>
    public async Task<CreateInvitationResult> CreateAsync(
        CreateInvitationRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireAccessAsync(
            CapabilityCodes.UserInvitationsWrite,
            cancellationToken).ConfigureAwait(false);
        string email = RequireEmail(request.Email);
        await RequireProfileSnapshotAsync(
            request.ProfileSnapshot,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        string token = NewLinkToken();
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Email = email,
            HospitalId = hospitalContext.HospitalId,
            ProfileSnapshot = request.ProfileSnapshot,
            TokenHash = InvitationTokenHasher.Hash(token),
            IssuedAt = now,
            ExpiresAt = now.Add(LinkValidity),
            CreatedAt = now,
        };
        invitations.Add(invitation);
        StageAudit(invitation, "invitation.created", actorId, now);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return new CreateInvitationResult(MapView(invitation), token);
    }

    /// <summary>Cancels a pending or expired invitation immediately.</summary>
    public async Task<InvitationView> CancelAsync(
        Guid id,
        string? actorId,
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(
            CapabilityCodes.UserInvitationsWrite,
            cancellationToken).ConfigureAwait(false);
        Invitation invitation = await RequireInvitationAsync(id, cancellationToken)
            .ConfigureAwait(false);
        InvitationExpiryNotice? expiryNotice = await ExpireIfDueAsync(
            invitation, actorId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Cancel);
        invitation.CancelledAt = now;
        StageAudit(invitation, "invitation.cancelled", actorId, now);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (expiryNotice is not null)
        {
            await NotifyExpiredAsync([expiryNotice], cancellationToken)
                .ConfigureAwait(false);
        }

        return MapView(invitation);
    }

    /// <summary>
    /// Supersedes the current link: bumps <see cref="Invitation.LinkVersion"/>,
    /// rehashes a new opaque token, and restarts the 72-hour window.
    /// </summary>
    public async Task<CreateInvitationResult> ResendAsync(
        Guid id,
        string? actorId,
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(
            CapabilityCodes.UserInvitationsWrite,
            cancellationToken).ConfigureAwait(false);
        Invitation invitation = await RequireInvitationAsync(id, cancellationToken)
            .ConfigureAwait(false);
        InvitationExpiryNotice? expiryNotice = await ExpireIfDueAsync(
            invitation, actorId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        InvitationLifecycle.Fire(invitation, InvitationLifecycle.Trigger.Resend);
        string token = NewLinkToken();
        invitation.LinkVersion++;
        invitation.TokenHash = InvitationTokenHasher.Hash(token);
        invitation.IssuedAt = now;
        invitation.ExpiresAt = now.Add(LinkValidity);
        StageAudit(invitation, "invitation.resent", actorId, now);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (expiryNotice is not null)
        {
            await NotifyExpiredAsync([expiryNotice], cancellationToken)
                .ConfigureAwait(false);
        }

        return new CreateInvitationResult(MapView(invitation), token);
    }

    /// <summary>
    /// Lists the hospital's invitations newest-first, lazily expiring due
    /// entries inside the same commit before returning views.
    /// </summary>
    public async Task<IReadOnlyList<InvitationView>> ListAsync(
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(
            CapabilityCodes.UserInvitationsRead,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Invitation> rows = await invitations
            .ListByHospitalAsync(hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        List<InvitationExpiryNotice> notices = [];
        foreach (Invitation invitation in rows)
        {
            InvitationExpiryNotice? notice = await ExpireIfDueAsync(
                invitation, actorId: null, cancellationToken)
                .ConfigureAwait(false);
            if (notice is not null)
            {
                notices.Add(notice);
            }
        }

        if (notices.Count > 0)
        {
            _ = await unitOfWork.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await NotifyExpiredAsync(notices, cancellationToken)
                .ConfigureAwait(false);
        }

        return [.. rows.Select(MapView)];
    }

    private async Task RequireAccessAsync(
        string capability,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(capability, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects snapshots that do not conform to the canonical
    /// profile-snapshot contract. A null snapshot stays valid: acceptance
    /// grants nothing but the membership when no snapshot is attached.
    /// </summary>
    private async Task RequireProfileSnapshotAsync(
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        if (snapshotJson is null)
        {
            return;
        }

        IReadOnlyList<string> errors = await profileSnapshotValidator
            .ValidateAsync(snapshotJson, cancellationToken)
            .ConfigureAwait(false);
        if (errors.Count > 0)
        {
            throw new ValidationException(
                $"Invalid profile snapshot: {string.Join("; ", errors)}");
        }
    }

    private async Task<Invitation> RequireInvitationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Invitation? invitation = await invitations
            .FindByIdAsync(id, track: true, cancellationToken)
            .ConfigureAwait(false);
        if (invitation is null
            || invitation.HospitalId != hospitalContext.HospitalId)
        {
            throw new NotFoundException($"Invitation '{id}' was not found.");
        }

        return invitation;
    }

    /// <summary>Lazily expires a due invitation; stages its audit.</summary>
    private async Task<InvitationExpiryNotice?> ExpireIfDueAsync(
        Invitation invitation,
        string? actorId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        bool expired = await expiryEvaluator
            .EvaluateAsync(invitation, now, cancellationToken)
            .ConfigureAwait(false);
        if (!expired)
        {
            return null;
        }

        StageAudit(invitation, "invitation.expired", actorId, now);
        return new InvitationExpiryNotice(
            invitation.Id,
            invitation.Email,
            invitation.HospitalId,
            invitation.LinkVersion);
    }

    private async Task NotifyExpiredAsync(
        List<InvitationExpiryNotice> notices,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> recipients = await invitations
            .FindExpiryNotificationRecipientsAsync(
                notices[0].HospitalId,
                cancellationToken).ConfigureAwait(false);
        foreach (InvitationExpiryNotice notice in notices)
        {
            await notifier.InvitationExpiredAsync(
                notice,
                recipients,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void StageAudit(
        Invitation invitation,
        string action,
        string? actorId,
        DateTimeOffset occurredAt)
    {
        auditWriter.Append(
            AuditEntityTypes.Invitation,
            invitation.Id,
            action,
            actorId,
            occurredAt,
            new { email = invitation.Email, invitation.LinkVersion });
    }

    private static InvitationView MapView(Invitation invitation)
    {
        return new(
            invitation.Id,
            invitation.Email,
            invitation.HospitalId,
            invitation.Status.ToString(),
            invitation.LinkVersion,
            invitation.CreatedAt,
            invitation.IssuedAt,
            invitation.ExpiresAt);
    }

    private static string RequireEmail(string? email)
    {
        string normalized = email?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ValidationException(
                "email is required to invite a member.");
        }

        return normalized;
    }

    private static string NewLinkToken()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
    }
}
