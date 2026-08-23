using Cynara.Infrastructure.Modules.Identity;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Cynara.Api.Tests.Support;

public sealed class PostgreSqlDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(
            "postgres:17-alpine")
        .Build();

    public TestDatabaseSettings Settings { get; private set; } =
        TestDatabaseSettings.FromConnectionString(string.Empty);

    public async Task InitializeAsync()
    {
        await container.StartAsync().ConfigureAwait(false);
        Settings = TestDatabaseSettings.FromConnectionString(
            container.GetConnectionString());
    }

    public Task DisposeAsync()
    {
        return container.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Truncates every table in the shared Postgres container (schema and
    /// sequences preserved so concurrent test classes keep working) and
    /// excludes both EF migration-history tables so applied stamps stay valid.
    /// Tests bypassing the WebApplicationFactory should call this from setup.
    /// </summary>
    public async Task ResetAsync()
    {
        const string MigrationHistoryTable = "__EFMigrationsHistory";
        const string IdentityMigrationHistoryTable =
            CynaraIdentityDbContext.MigrationsHistoryTableName;
        const string TruncateSql = @"
DO $$
DECLARE
    tables_to_truncate TEXT;
BEGIN
    SELECT string_agg(format('%I.%I', schemaname, tablename), ', ' ORDER BY tablename)
        INTO tables_to_truncate
        FROM pg_tables
        WHERE schemaname = current_schema()
          AND tablename <> '" + MigrationHistoryTable + @"'
          AND tablename <> '" + IdentityMigrationHistoryTable + @"';

    IF tables_to_truncate IS NOT NULL THEN
        EXECUTE 'TRUNCATE TABLE ' || tables_to_truncate || ' RESTART IDENTITY CASCADE';
    END IF;
END $$;";

        await using NpgsqlConnection connection = new(Settings.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = TruncateSql;
        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
