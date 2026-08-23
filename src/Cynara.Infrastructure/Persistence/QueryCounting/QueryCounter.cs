namespace Cynara.Infrastructure.Persistence.QueryCounting;

/// <summary>
/// Scoped, thread-safe counter of executed read commands for the current
/// request, incremented by <see cref="QueryCountingInterceptor"/> so an N+1
/// regression is observable as a number instead of silent wire chatter.
/// </summary>
public sealed class QueryCounter
{
    private int count;

    /// <summary>Read commands executed so far in this scope.</summary>
    public int Count => Volatile.Read(ref count);

    public void Increment()
    {
        _ = Interlocked.Increment(ref count);
    }
}
