namespace Cynara.Application.Modules.Invitations.Persistence;

/// <summary>
/// Coordinates the single PostgreSQL transaction shared by both DbContexts
/// during acceptance. Begin attaches the identity context to the domain
/// context's transaction (one physical connection per request — no 2PC);
/// disposing without a commit rolls back.
/// </summary>
public interface IInvitationAcceptanceTransaction : IAsyncDisposable
{
    public Task BeginAsync(CancellationToken cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Idempotent; safe to call before disposal.</summary>
    public Task RollbackAsync(CancellationToken cancellationToken);
}
