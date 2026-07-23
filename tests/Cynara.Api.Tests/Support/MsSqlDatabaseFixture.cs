using Testcontainers.MsSql;

namespace Cynara.Api.Tests.Support;

public sealed class MsSqlDatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .Build();

    public TestDatabaseSettings Settings { get; private set; } =
        TestDatabaseSettings.SqliteInMemory;

    public async Task InitializeAsync()
    {
        await container.StartAsync().ConfigureAwait(false);
        Settings = TestDatabaseSettings.SqlServer(container.GetConnectionString());
    }

    public Task DisposeAsync()
    {
        return container.DisposeAsync().AsTask();
    }
}
