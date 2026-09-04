using Cynara.Application.Modules.Invitations.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Invitations;

namespace Cynara.Infrastructure.Modules.Invitations;

/// <summary>
/// EF Core implementation of the invitation repository; tracked or
/// untracked reads as requested for row-version concurrency checks, and
/// nothing commits here — the owning workflow persists mutations together.
/// </summary>
public sealed class InvitationRepository(CynaraDbContext dbContext)
    : IInvitationRepository
{
    public void Add(Invitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        _ = dbContext.Invitations.Add(invitation);
    }

    public Task<Invitation?> FindByIdAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Invitation> query = track
            ? dbContext.Invitations
            : dbContext.Invitations.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);
    }

    public Task<Invitation?> FindByTokenHashAsync(
        string tokenHash,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Invitation> query = track
            ? dbContext.Invitations
            : dbContext.Invitations.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.TokenHash == tokenHash,
            cancellationToken);
    }

    public async Task LockForUpdateAsync(
        Invitation invitation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        // Pessimistic row lock: Npgsql 10 removed the queryable ForUpdate
        // helpers, so the lock is a raw SELECT ... FOR UPDATE. The lock is
        // held until the caller's transaction commits or rolls back.
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "invitations" WHERE "Id" = {invitation.Id} FOR UPDATE""",
            cancellationToken).ConfigureAwait(false);
        await dbContext.Entry(invitation).ReloadAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Invitation>> ListByHospitalAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Invitations
            .Where(item => item.HospitalId == hospitalId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>>
        FindExpiryNotificationRecipientsAsync(
            Guid hospitalId,
            CancellationToken cancellationToken)
    {
        return await dbContext.CapabilityAssignments
            .Where(item => item.Capability
                == CapabilityCodes.UserInvitationsRead
                && item.Scope == CapabilityScopes.Hospital
                && item.HospitalId == hospitalId)
            .Select(item => item.ActorId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
