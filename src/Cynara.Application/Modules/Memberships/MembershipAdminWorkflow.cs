using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Memberships;

namespace Cynara.Application.Modules.Memberships;

/// <summary>
/// Administrative membership lifecycle workflows (slice 2: add, update,
/// list; revoke/reactivate arrive in slice 3). Every mutation runs inside
/// one cross-track transaction so the identity row and the staged domain
/// audit commit atomically. Format violations surface 400, unknown users
/// 404, and actor-taken or cardinality conflicts surface 409 — a
/// deliberate divergence from invitation acceptance's 400 (do NOT "fix").
/// </summary>
public sealed class MembershipAdminWorkflow(
    IMembershipRepository memberships,
    ICrossTrackTransaction transaction,
    ICapabilityGuard capabilityGuard,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Creates one Active row for the requested user in the caller's
    /// hospital with an atomic <c>membership.added</c> audit event.
    /// </summary>
    public async Task<MembershipView> AddAsync(
        AddMembershipRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireAccessAsync(
            CapabilityCodes.MembershipsWrite,
            cancellationToken).ConfigureAwait(false);
        string actor = ActorIdValidator.RequireValid(request.ActorId);
        Guid hospitalId = hospitalContext.HospitalId;
        if (!await memberships.UserExistsAsync(
                request.UserId,
                cancellationToken).ConfigureAwait(false))
        {
            throw new NotFoundException(
                $"User '{request.UserId}' was not found.");
        }

        if (await memberships.HasActiveAsync(
                request.UserId,
                hospitalId,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                "The user already has an active membership in "
                + "this hospital.");
        }

        if (await memberships.IsActorIdActiveAsync(
                hospitalId,
                actor,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Actor id '{actor}' is already in use in "
                + "this hospital.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        await using (transaction.ConfigureAwait(false))
        {
            await transaction.BeginAsync(cancellationToken)
                .ConfigureAwait(false);
            MembershipRow created = memberships.Add(
                request.UserId,
                hospitalId,
                actor,
                now);
            auditWriter.Append(
                AuditEntityTypes.Membership,
                created.Id,
                "membership.added",
                actorId,
                now,
                new { userId = request.UserId, actorId = actor });
            _ = await unitOfWork.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = await memberships.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            return MapView(created);
        }
    }

    /// <summary>
    /// Replaces the actor id of an Active row: the current row becomes
    /// Revoked and a new Active row is inserted in one transaction with
    /// an atomic <c>membership.updated</c> audit event.
    /// </summary>
    public async Task<MembershipView> UpdateAsync(
        Guid id,
        UpdateMembershipRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireAccessAsync(
            CapabilityCodes.MembershipsWrite,
            cancellationToken).ConfigureAwait(false);
        string actor = ActorIdValidator.RequireValid(request.ActorId);
        MembershipRow current = await RequireActiveAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (await memberships.IsActorIdActiveAsync(
                hospitalContext.HospitalId,
                actor,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Actor id '{actor}' is already in use in "
                + "this hospital.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        await using (transaction.ConfigureAwait(false))
        {
            await transaction.BeginAsync(cancellationToken)
                .ConfigureAwait(false);
            MembershipRow next = await memberships.ReplaceActorAsync(
                current.Id,
                actor,
                now,
                cancellationToken).ConfigureAwait(false);
            auditWriter.Append(
                AuditEntityTypes.Membership,
                next.Id,
                "membership.updated",
                actorId,
                now,
                new
                {
                    previousMembershipId = current.Id,
                    previousActorId = current.ActorId,
                    actorId = actor,
                });
            _ = await unitOfWork.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = await memberships.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            return MapView(next);
        }
    }

    /// <summary>
    /// Lists the caller's hospital memberships newest-first, history
    /// included with status exposed.
    /// </summary>
    public async Task<IReadOnlyList<MembershipView>> ListAsync(
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(
            CapabilityCodes.MembershipsRead,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MembershipRow> rows = await memberships
            .ListByHospitalAsync(
                hospitalContext.HospitalId,
                cancellationToken)
            .ConfigureAwait(false);
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

    private async Task<MembershipRow> RequireActiveAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        MembershipRow? current = await memberships
            .FindByIdAsync(id, track: false, cancellationToken)
            .ConfigureAwait(false);
        if (current is null
            || current.HospitalId != hospitalContext.HospitalId)
        {
            throw new NotFoundException(
                $"Membership '{id}' was not found.");
        }

        if (current.Status != MembershipStatus.Active)
        {
            throw new InvalidStateException(
                $"Membership '{id}' is not active.");
        }

        return current;
    }

    private static MembershipView MapView(MembershipRow row)
    {
        return new(
            row.Id,
            row.UserId,
            row.ActorId,
            row.Status.ToString(),
            row.CreatedAt,
            row.ActivatedAt,
            row.RevokedAt,
            row.UpdatedAt);
    }
}
