namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> that always returns the same
/// instant. The catalog workflow stamps creator/last-write timestamps from
/// <see cref="TimeProvider"/>, so tests need a frozen clock to assert
/// equality reliably.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset now)
    {
        Now = now;
    }

    public DateTimeOffset Now { get; }

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}
