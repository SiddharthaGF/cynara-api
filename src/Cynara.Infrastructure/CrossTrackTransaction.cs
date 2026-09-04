using Cynara.Application.Persistence;
using Cynara.Infrastructure.Modules.Identity;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Storage;

namespace Cynara.Infrastructure;

/// <summary>
/// Shared-connection transaction coordinator: begins one explicit
/// PostgreSQL transaction on the domain context and attaches the identity
/// context to it, so both tracks commit atomically over a single physical
/// connection (no 2PC). The workflow owns disposal through
/// <see cref="IAsyncDisposable"/>; the DI container may dispose the
/// scoped instance again at scope end, so <see cref="DisposeAsync"/>
/// nulls the underlying transaction and becomes a no-op on the second
/// pass — the repeated rollback/dispose sequence is always safe.
/// </summary>
public sealed class CrossTrackTransaction(
    CynaraDbContext domainDbContext,
    CynaraIdentityDbContext identityDbContext)
    : ICrossTrackTransaction
{
    private IDbContextTransaction? transaction;

    private bool completed;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException(
                "The cross-track transaction has already begun.");
        }

        transaction = await domainDbContext.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _ = await identityDbContext.Database
            .UseTransactionAsync(
                transaction.GetDbTransaction(),
                cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            throw new InvalidOperationException(
                "The cross-track transaction has not begun.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        completed = true;
        await DetachAsync().ConfigureAwait(false);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (transaction is null || completed)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken)
            .ConfigureAwait(false);
        completed = true;
        await DetachAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        IDbContextTransaction? current = transaction;
        transaction = null;
        if (current is null)
        {
            return;
        }

        if (!completed)
        {
            await current.RollbackAsync().ConfigureAwait(false);
            completed = true;
        }

        await current.DisposeAsync().ConfigureAwait(false);
        await DetachAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Detaches both contexts from the finished transaction so later saves
    /// in the same scope never touch a disposed transaction.
    /// </summary>
    private async Task DetachAsync()
    {
        _ = await domainDbContext.Database
            .UseTransactionAsync(transaction: null).ConfigureAwait(false);
        _ = await identityDbContext.Database
            .UseTransactionAsync(transaction: null).ConfigureAwait(false);
    }
}
