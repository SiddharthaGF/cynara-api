using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;
using Cynara.Domain.Invitations;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Public single-step acceptance: turns a one-time invitation token into
/// credentials and a hospital membership in one cross-track atomic commit.
/// Token state stays unenumerable — every token-state outcome returns the
/// same uniform envelope; only visible request failures (weak password,
/// taken actor id, existing membership) surface as 400. All non-acceptance
/// outcomes keep single-save semantics; the acceptance branch relaxes the
/// single-<c>SaveChangesAsync</c> rule inside one shared transaction.
/// </summary>
public sealed class InvitationAcceptanceWorkflow(
    InvitationAcceptancePersistence persistence,
    InvitationAcceptanceContext context)
{
    private const int MaxNameLength = 128;

    // Process-local stripes serializing concurrent accepts of the same
    // link. The database stays the cross-instance backstop (unique
    // constraints plus the RowVersion concurrency token); this only
    // restores the single-winner ordering inside one process, using the EF
    // model alone and no hand-written SQL. Fixed bucket count bounds the
    // state; no per-token keys are retained.
    private static readonly SemaphoreSlim[] AcceptanceStripes = [.. Enumerable
        .Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1))];

    /// <summary>Accepts the invitation or returns the uniform envelope.</summary>
    public async Task<AcceptInvitationResponse> AcceptAsync(
        string token,
        AcceptInvitationRequest? request,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim stripe =
            AcceptanceStripes[
                (uint)StringComparer.Ordinal.GetHashCode(token)
                % (uint)AcceptanceStripes.Length];
        await stripe.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AcceptInnerAsync(token, request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _ = stripe.Release();
        }
    }

    private async Task<AcceptInvitationResponse> AcceptInnerAsync(
        string token,
        AcceptInvitationRequest? request,
        CancellationToken cancellationToken)
    {
        Invitation? invitation = await FindInvitationAsync(
            token, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return AcceptInvitationResponse.Failure;
        }

        Hospital? hospital = await context.Hospitals.FindByIdAsync(
            invitation.HospitalId,
            track: false,
            cancellationToken).ConfigureAwait(false);
        if (hospital is null)
        {
            return AcceptInvitationResponse.Failure;
        }

        HospitalContext settableContext =
            context.HospitalContext as HospitalContext
            ?? throw new InvalidOperationException(
                "Hospital context is not settable for acceptance.");
        settableContext.SetWorkspace(
            hospital.Id, hospital.Code, hospital.Name);
        DateTimeOffset now = context.TimeProvider.GetUtcNow();

        bool expired = await persistence.ExpiryEvaluator.EvaluateAsync(
            invitation, now, cancellationToken).ConfigureAwait(false);
        if (expired)
        {
            StageAudit(invitation, "invitation.expired", now);
            _ = await persistence.UnitOfWork.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            return AcceptInvitationResponse.Failure;
        }

        switch (invitation.Status)
        {
            case InvitationStatus.Accepted:
                InvitationLifecycle.Fire(
                    invitation, InvitationLifecycle.Trigger.AlreadyUsed);
                StageAudit(invitation, "invitation.already-used", now);
                _ = await persistence.UnitOfWork.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
                return AcceptInvitationResponse.Failure;
            case InvitationStatus.AlreadyUsed:
            case InvitationStatus.Expired:
            case InvitationStatus.Revoked:
            case InvitationStatus.Cancelled:
                return AcceptInvitationResponse.Failure;
            case InvitationStatus.Pending:
                break;
            default:
                break;
        }

        ParsedProfileSnapshot? snapshot = await ParseSnapshotAsync(
            invitation, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            StageAudit(invitation, "invitation.acceptance-failed", now);
            _ = await persistence.UnitOfWork.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            return AcceptInvitationResponse.Failure;
        }

        // Request validation runs only after the token resolved to a valid
        // pending invitation: visible-field failures (missing or weak
        // password) surface as 400, token-state outcomes stay uniform.
        string password = RequirePassword(request);
        IReadOnlyList<string> passwordErrors = await persistence.IdentityStore
            .ValidatePasswordAsync(
                invitation.Email,
                password,
                cancellationToken).ConfigureAwait(false);
        if (passwordErrors.Count > 0)
        {
            throw new ValidationException(
                "The password does not meet the policy: "
                + string.Join("; ", passwordErrors));
        }

        // Member names come from the accept request first and fall back to
        // the administrator-predefined snapshot profile. When neither side
        // provides them the request visibly fails so the invitee can supply
        // them; token-state outcomes stay uniform.
        (string givenName, string familyName) = RequireNames(request, snapshot);

        return await AcceptPendingAsync(
            invitation,
            snapshot,
            password,
            givenName,
            familyName,
            hospital,
            now,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AcceptInvitationResponse> AcceptPendingAsync(
        Invitation invitation,
        ParsedProfileSnapshot snapshot,
        string password,
        string givenName,
        string familyName,
        Hospital hospital,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (persistence.Transaction.ConfigureAwait(false))
        {
            await persistence.Transaction.BeginAsync(cancellationToken)
                .ConfigureAwait(false);

            // Re-verify under a row lock: a concurrent winner may have
            // transitioned the link between the initial read and the
            // transaction start. The lock serializes the race, so the loser
            // folds into the token-state outcomes instead of observing the
            // winner's identity rows and surfacing a spurious 400.
            await persistence.Invitations.LockForUpdateAsync(
                invitation, cancellationToken).ConfigureAwait(false);
            if (invitation.Status != InvitationStatus.Pending)
            {
                if (invitation.Status == InvitationStatus.Accepted)
                {
                    InvitationLifecycle.Fire(
                        invitation, InvitationLifecycle.Trigger.AlreadyUsed);
                    StageAudit(invitation, "invitation.already-used", now);
                    _ = await persistence.UnitOfWork.SaveChangesAsync(
                        cancellationToken).ConfigureAwait(false);
                }

                await persistence.Transaction.CommitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return AcceptInvitationResponse.Failure;
            }

            Guid? existingUserId = await persistence.IdentityStore
                .FindUserIdByEmailAsync(
                    invitation.Email, cancellationToken).ConfigureAwait(false);
            if (existingUserId is not null
                && await persistence.IdentityStore.HasMembershipAsync(
                    existingUserId.Value,
                    invitation.HospitalId,
                    cancellationToken).ConfigureAwait(false))
            {
                await persistence.Transaction.RollbackAsync(cancellationToken)
                    .ConfigureAwait(false);
                throw new ValidationException(
                    "The invited user already belongs to this hospital.");
            }

            if (await persistence.IdentityStore.IsActorIdTakenAsync(
                    invitation.HospitalId,
                    snapshot.ActorId,
                    cancellationToken).ConfigureAwait(false))
            {
                await persistence.Transaction.RollbackAsync(cancellationToken)
                    .ConfigureAwait(false);
                throw new ValidationException(
                    $"Actor id '{snapshot.ActorId}' is already in use in "
                    + "this hospital.");
            }

            Guid userId = await ResolveUserIdAsync(
                    existingUserId,
                    invitation,
                    password,
                    givenName,
                    familyName,
                    cancellationToken)
                .ConfigureAwait(false);

            await persistence.IdentityStore.AddMembershipAsync(
                userId,
                invitation.HospitalId,
                snapshot.ActorId,
                now,
                cancellationToken).ConfigureAwait(false);
            foreach (string code in snapshot.Capabilities)
            {
                persistence.Grants.Add(new CapabilityAssignment
                {
                    Id = Guid.NewGuid(),
                    HospitalId = invitation.HospitalId,
                    ActorId = snapshot.ActorId,
                    Capability = code,
                    Scope = CapabilityScopes.Hospital,
                    AssignedAt = now,
                    AssignedBy = null,
                });
            }

            InvitationLifecycle.Fire(
                invitation, InvitationLifecycle.Trigger.Accept);
            invitation.UserId = userId;
            invitation.AcceptedAt = now;
            StageAudit(invitation, "invitation.accepted", now);

            _ = await persistence.UnitOfWork.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await persistence.IdentityStore.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await persistence.Transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);

            return new AcceptInvitationResponse(
                Accepted: true,
                new MemberSummary(
                    new UserSummary(userId, invitation.Email),
                    new HospitalSummary(
                        hospital.Id, hospital.Code, hospital.Name),
                    new ActorSummary(snapshot.ActorId),
                    snapshot.Capabilities));
        }
    }

    private async Task<Guid> ResolveUserIdAsync(
        Guid? existingUserId,
        Invitation invitation,
        string password,
        string givenName,
        string familyName,
        CancellationToken cancellationToken)
    {
        if (existingUserId is not null)
        {
            // Existing accounts keep their stored names; only blank slots
            // are backfilled from this acceptance.
            await persistence.IdentityStore.FillMissingNamesAsync(
                    existingUserId.Value,
                    givenName,
                    familyName,
                    cancellationToken)
                .ConfigureAwait(false);
            return existingUserId.Value;
        }

        CreateUserResult created = await persistence.IdentityStore.CreateUserAsync(
            invitation.Email,
            password,
            givenName,
            familyName,
            cancellationToken).ConfigureAwait(false);
        if (created.Duplicate)
        {
            return (await persistence.IdentityStore.FindUserIdByEmailAsync(
                    invitation.Email, cancellationToken).ConfigureAwait(false))
                ?? throw new InvalidOperationException(
                    "Duplicate email resolved to no user after create.");
        }

        if (created.UserId is null)
        {
            await persistence.Transaction.RollbackAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new ValidationException(
                "Could not create the invited account: "
                + string.Join("; ", created.Errors));
        }

        return created.UserId.Value;
    }

    /// <summary>
    /// Defensive snapshot parse: missing, malformed, non-conforming, or
    /// unsupported snapshots all fail closed.
    /// </summary>
    private async Task<ParsedProfileSnapshot?> ParseSnapshotAsync(
        Invitation invitation,
        CancellationToken cancellationToken)
    {
        if (invitation.ProfileSnapshot is null)
        {
            return null;
        }

        IReadOnlyList<string> errors = await context.SnapshotValidator
            .ValidateAsync(
                invitation.ProfileSnapshot, cancellationToken)
            .ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return null;
        }

        return InvitationProfileSnapshotParser.TryParse(
            invitation.ProfileSnapshot);
    }

    private async Task<Invitation?> FindInvitationAsync(
        string token,
        CancellationToken cancellationToken)
    {
        string tokenHash = InvitationTokenHasher.Hash(token);
        return await persistence.Invitations.FindByTokenHashAsync(
            tokenHash, track: true, cancellationToken).ConfigureAwait(false);
    }

    private void StageAudit(
        Invitation invitation,
        string action,
        DateTimeOffset occurredAt)
    {
        context.AuditWriter.Append(
            AuditEntityTypes.Invitation,
            invitation.Id,
            action,
            actorId: null,
            occurredAt,
            new { email = invitation.Email, invitation.LinkVersion });
    }

    private static string RequirePassword(AcceptInvitationRequest? request)
    {
        string? password = request?.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ValidationException(
                "password is required to accept an invitation.");
        }

        return password;
    }

    /// <summary>
    /// Resolves the member's names from the accept request first, falling
    /// back to the administrator-predefined snapshot profile. Throws a
    /// visible <see cref="ValidationException"/> when either side is
    /// missing or over the column limit so the invitee can supply them.
    /// Web clients match on "given name and family name" to reveal the
    /// name fields.
    /// </summary>
    private static (string GivenName, string FamilyName) RequireNames(
        AcceptInvitationRequest? request,
        ParsedProfileSnapshot snapshot)
    {
        string givenName = FirstNonBlank(request?.Name, snapshot.GivenName);
        string familyName = FirstNonBlank(request?.Surname, snapshot.FamilyName);
        if (givenName.Length == 0 || familyName.Length == 0)
        {
            throw new ValidationException(
                "The invited member's given name and family name are "
                + "required to accept this invitation.");
        }

        if (givenName.Length > MaxNameLength || familyName.Length > MaxNameLength)
        {
            throw new ValidationException(
                $"Given name and family name must be 1-{MaxNameLength} "
                + "characters.");
        }

        return (givenName, familyName);
    }

    private static string FirstNonBlank(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first.Trim();
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second.Trim();
        }

        return string.Empty;
    }
}
