using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cynara.Infrastructure.Persistence.QueryCounting;

/// <summary>
/// EF Core interceptor counting every executed read command on the current
/// <see cref="QueryCounter"/>; counting readers is what surfaces N+1
/// patterns. Registered scoped so the count dies with the request scope.
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
