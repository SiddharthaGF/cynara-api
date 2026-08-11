using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cynara.Infrastructure.Persistence.QueryCounting;

/// <summary>
/// EF Core interceptor that counts every executed read command on the
/// current <see cref="QueryCounter"/>. Counting readers (not writes) is what
/// surfaces N+1 patterns: one logical request that issues one SELECT followed
/// by one SELECT per row appears as an unbounded count, while an eager-loaded
/// projection stays at a small constant. Registered scoped alongside the
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> so the counter is
/// per-request and the count dies with the scope.
/// </summary>
public sealed class QueryCountingInterceptor(QueryCounter counter)
    : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        counter.Increment();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.ReaderExecutingAsync(
            command,
            eventData,
            result,
            cancellationToken);
    }
}
