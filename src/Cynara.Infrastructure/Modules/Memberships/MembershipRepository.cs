using Cynara.Application;
using Cynara.Application.Modules.Memberships;
using Cynara.Domain.Memberships;
using Cynara.Infrastructure.Modules.Identity;

namespace Cynara.Infrastructure.Modules.Memberships;

/// <summary>
/// EF Core implementation of the membership repository over the identity
/// track; tracked or untracked reads as requested for row-version
/// concurrency checks, and nothing commits here — the owning workflow
/// persists mutations together with staged audit events. Slice 2 covers
/// add/update/list; revoke/reactivate arrive in slice 3.
/// </summary>
public sealed class MembershipRepository(
    CynaraIdentityDbContext identityDbContext)
    : IMembershipRepository
{
    public async Task<MembershipRow?> FindByIdAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        Membership? membership = await Query(track)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken)
            .ConfigureAwait(false);
        return membership is null ? null : MapRow(membership);
    }

    public async Task<IReadOnlyList<MembershipRow>> ListByHospitalAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        List<Membership> memberships = await identityDbContext
            .Memberships
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. memberships.Select(MapRow)];
    }

    public Task<bool> IsActorIdActiveAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken)
    {
        return identityDbContext.Memberships
            .AsNoTracking()
            .AnyAsync(
                item => item.HospitalId == hospitalId
                    && item.ActorId == actorId
                    && item.Status == MembershipStatus.Active,
                cancellationToken);
    }

    public Task<bool> HasActiveAsync(
        Guid userId,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return identityDbContext.Memberships
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId
                    && item.HospitalId == hospitalId
                    && item.Status == MembershipStatus.Active,
                cancellationToken);
    }

    public Task<bool> UserExistsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return identityDbContext.Users
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId, cancellationToken);
    }

    public MembershipRow Add(
        Guid userId,
        Guid hospitalId,
        string actorId,
        DateTimeOffset now)
    {
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HospitalId = hospitalId,
            ActorId = actorId,
            CreatedAt = now,
            Status = MembershipStatus.Active,
            ActivatedAt = now,
            UpdatedAt = now,
        };
        _ = identityDbContext.Memberships.Add(membership);
        return MapRow(membership);
    }

    public async Task<MembershipRow> ReplaceActorAsync(
        Guid currentId,
        string newActorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Membership? current = await identityDbContext.Memberships
            .SingleOrDefaultAsync(
                item => item.Id == currentId,
                cancellationToken)
            .ConfigureAwait(false) ?? throw new NotFoundException(
                $"Membership '{currentId}' was not found.");
        current.Status = MembershipStatus.Revoked;
        current.RevokedAt = now;
        current.UpdatedAt = now;
        current.RowVersion++;
        return Add(current.UserId, current.HospitalId, newActorId, now);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Membership> Query(bool track)
    {
        return track
            ? identityDbContext.Memberships
            : identityDbContext.Memberships.AsNoTracking();
    }

    private static MembershipRow MapRow(Membership membership)
    {
        return new(
            membership.Id,
            membership.UserId,
            membership.HospitalId,
            membership.ActorId,
            membership.Status,
            membership.CreatedAt,
            membership.ActivatedAt,
            membership.RevokedAt,
            membership.UpdatedAt,
            membership.RowVersion);
    }
}
