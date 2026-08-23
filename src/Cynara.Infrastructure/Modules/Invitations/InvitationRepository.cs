using Cynara.Application.Modules.Invitations.Persistence;
using Cynara.Domain.Invitations;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Invitations;

/// <summary>
/// EF Core implementation of the invitation repository. Reads are tracked
/// or untracked as requested so lifecycle workflows get row-version
/// concurrency checks; nothing here commits — the owning workflow's single
/// <c>SaveChangesAsync</c> persists mutations together with staged audit.
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
}
