using Cynara.Application;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Creates an isolated EF Core In-Memory database for a single integration
/// test run. Each factory owns its own database name, so concurrent test
/// classes do not share state. In-Memory is appropriate because every
/// concurrency check, query filter, and schema constraint exercised by the
/// tests is asserted through the HTTP layer (or via explicit
/// <see cref="CynaraException"/> paths) — none of the tests rely on the
/// database engine rejecting invalid inserts, so the loss of FK enforcement
/// does not weaken the suite.
/// </summary>
public sealed class InMemoryTestDatabaseFactory
{
    private static int databaseCounter;

    private InMemoryTestDatabaseFactory(string databaseName, DbContextOptions<CynaraDbContext> options)
    {
        DatabaseName = databaseName;
        ContextOptions = options;
    }

    public string DatabaseName { get; }

    public DbContextOptions<CynaraDbContext> ContextOptions { get; }

    public static InMemoryTestDatabaseFactory Create()
    {
        int id = Interlocked.Increment(ref databaseCounter);
        string databaseName = string.Concat("CynaraTests_", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        DbContextOptions<CynaraDbContext> options =
            new DbContextOptionsBuilder<CynaraDbContext>()
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings =>
                    _ = warnings.Ignore(
                        InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        return new InMemoryTestDatabaseFactory(databaseName, options);
    }

    public CynaraDbContext CreateDbContext()
    {
        return new CynaraDbContext(ContextOptions);
    }
}
