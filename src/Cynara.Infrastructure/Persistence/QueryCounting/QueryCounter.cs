namespace Cynara.Infrastructure.Persistence.QueryCounting;

/// <summary>
/// Scoped, thread-safe counter of executed read commands for the current
/// request. Incremented by <see cref="QueryCountingInterceptor"/> and
/// surfaced to callers (HTTP middleware, integration tests) so a regression
/// that turns a bounded read path into an N+1 query explosion is observable
/// as a number instead of silent wire chatter.
/// </summary>
public sealed class QueryCounter
{
    private int count;

    /// <summary>Number of read commands executed so far in this scope.</summary>
    public int Count => Volatile.Read(ref count);

    public void Increment()
    {
        _ = Interlocked.Increment(ref count);
    }
}
