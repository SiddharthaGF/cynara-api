using Cynara.Domain.Invitations;

namespace Cynara.Application.Modules.Invitations.Persistence;

/// <summary>
/// Persistence port for invitations. Implementations track or read without
/// tracking as requested but NEVER commit: workflows own the single
/// <c>SaveChangesAsync</c> boundary that also commits staged audit events.
/// </summary>
public interface IInvitationRepository
{
    public void Add(Invitation invitation);

    public Task<Invitation?> FindByIdAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an invitation by the hash of its CURRENT link token. Unique
    /// index on <see cref="Invitation.TokenHash"/> makes this an exact
    /// lookup; superseded tokens simply never match.
    /// </summary>
    public Task<Invitation?> FindByTokenHashAsync(
        string tokenHash,
        bool track,
        CancellationToken cancellationToken);
}
