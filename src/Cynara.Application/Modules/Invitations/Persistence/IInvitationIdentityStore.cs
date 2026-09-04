namespace Cynara.Application.Modules.Invitations.Persistence;

/// <summary>
/// Identity-track persistence port for invitation acceptance, implemented
/// over <c>UserManager&lt;IdentityUser&lt;Guid&gt;&gt;</c> and the identity
/// DbContext in Infrastructure. The Application layer stays EF- and
/// Identity-free; user, membership, and actor checks ride the shared
/// acceptance transaction when one is active.
/// </summary>
public interface IInvitationIdentityStore
{
    public Task<Guid?> FindUserIdByEmailAsync(
        string email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the registered password validators against the invited email;
    /// returns human-readable policy violations (empty when the password
    /// conforms).
    /// </summary>
    public Task<IReadOnlyList<string>> ValidatePasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the invited identity user with <c>EmailConfirmed</c> set, or
    /// reports a duplicate email so the caller falls back to the
    /// membership-only branch.
    /// </summary>
    public Task<CreateUserResult> CreateUserAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    public Task AddMembershipAsync(
        Guid userId,
        Guid hospitalId,
        string actorId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    public Task<bool> IsActorIdTakenAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken);

    public Task<bool> HasMembershipAsync(
        Guid userId,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken);
}
