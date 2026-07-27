using Cynara.Application.Persistence;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// Record-only unit of work that simulates the application's save boundary.
/// </summary>
public sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }
}
