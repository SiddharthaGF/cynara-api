using System.Data.Common;

using Cynara.Application.Failures;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Infrastructure.Failures;
using Cynara.Infrastructure.Modules.Audit;
using Cynara.Infrastructure.Modules.Components;
using Cynara.Infrastructure.Modules.FormAi;
using Cynara.Infrastructure.Modules.FormResponses;
using Cynara.Infrastructure.Modules.Forms;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Persistence;
using Cynara.Infrastructure.Schemas;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string SqliteProvider = "Sqlite";
    public const string SqlServerProvider = "SqlServer";

    private static readonly string[] RequiredTables =
    [
        "hospitals",
        "audit_events",
        "component_definitions",
        "component_versions",
        "form_definitions",
        "form_versions",
        "form_responses",
        "form_response_revisions",
        "ai_provider_settings",
        "failure_logs",
    ];

    public static IServiceCollection AddCynaraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        bool previewStorage = IsPreviewStorage(configuration);
        string databaseProvider = ResolveDatabaseProvider(
            configuration,
            previewStorage);
        string connectionString = ResolveConnectionString(
            configuration,
            previewStorage,
            databaseProvider);

        return services.AddCynaraInfrastructure(
            connectionString,
            SchemaFilePaths.FromBaseDirectory(),
            databaseProvider);
    }

    public static IServiceCollection AddCynaraInfrastructure(
        this IServiceCollection services,
        string connectionString,
        SchemaFilePaths schemaPaths,
        string databaseProvider = SqliteProvider)
    {
        _ = services.AddCynaraDatabase(connectionString, databaseProvider);
        _ = services.AddCynaraSchemas(schemaPaths);
        _ = services.AddCynaraPersistence();
        _ = services.AddFormAiInfrastructureModule();
        return services;
    }

    public static IServiceCollection AddCynaraDatabase(
        this IServiceCollection services,
        string connectionString,
        string databaseProvider = SqliteProvider)
    {
        if (string.Equals(connectionString, "Data Source=:memory:", StringComparison.OrdinalIgnoreCase))
        {
            _ = services.AddSingleton(_ =>
            {
                var connection = new SqliteConnection(connectionString);
                connection.Open();
                return connection;
            });
            _ = services.AddDbContext<CynaraDbContext>((provider, options) =>
                _ = options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        }
        else
        {
            _ = services.AddDbContext<CynaraDbContext>(options =>
            {
                if (IsSqlServer(databaseProvider))
                {
                    _ = options.UseSqlServer(connectionString);
                    return;
                }

                _ = options.UseSqlite(connectionString);
            });
        }

        _ = services.AddScoped<IUnitOfWork>(
            provider => provider.GetRequiredService<CynaraDbContext>());

        return services;
    }

    public static IServiceCollection AddCynaraSchemas(
        this IServiceCollection services,
        SchemaFilePaths schemaPaths)
    {
        _ = services.AddSingleton(schemaPaths);
        _ = services.AddSingleton<ISchemaValidator, JsonSchemaValidator>();

        return services;
    }

    public static IServiceCollection AddCynaraPersistence(
        this IServiceCollection services)
    {
        _ = services.AddHospitalsPersistenceModule();
        _ = services.AddAuditPersistenceModule();
        _ = services.AddComponentsPersistenceModule();
        _ = services.AddFormsPersistenceModule();
        _ = services.AddFormResponsesPersistenceModule();
        _ = services.AddFormAiPersistenceModule();
        _ = services.AddSingleton<IFailureLogWriter, FailureLogWriter>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            CynaraDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<CynaraDbContext>();

            if (dbContext.Database.IsSqlServer())
            {
                // EnsureCreated does not migrate an existing database, so any
                // entity added after the initial deploy (e.g. HospitalId for the
                // tenant workspace) would silently miss its schema. Try the
                // migrator first; if the history table is missing the migrator
                // raises InvalidOperationException, which we treat as a fresh
                // database and bootstrap via EnsureCreated. After EnsureCreated
                // we record the current migrations as applied so subsequent
                // deployments stay on the migrator path.
                try
                {
                    await dbContext.Database.MigrateAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    bool ensured = await dbContext.Database
                        .EnsureCreatedAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (ensured)
                    {
                        await SeedMigrationHistoryAsync(dbContext, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                return;
            }

            bool created = await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            if (!created && !await AllRequiredTablesExistAsync(dbContext, cancellationToken).ConfigureAwait(false))
            {
                _ = await dbContext.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
                _ = await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static bool IsSqlServer(string? databaseProvider)
    {
        return string.Equals(
            databaseProvider,
            SqlServerProvider,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPreviewStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return !string.Equals(
                configuration["VERCEL_ENV"],
                "production",
                StringComparison.OrdinalIgnoreCase) && (string.Equals(
                   configuration["CYNARA_ENV"],
                   "preview",
                   StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                configuration["VERCEL_ENV"],
                "preview",
                StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveDatabaseProvider(
        IConfiguration configuration,
        bool previewStorage)
    {
        return previewStorage
            ? SqliteProvider
            : configuration["Database:Provider"] ?? SqliteProvider;
    }

    private static string ResolveConnectionString(
        IConfiguration configuration,
        bool previewStorage,
        string databaseProvider)
    {
        if (previewStorage)
        {
            return "Data Source=:memory:";
        }

        string? configured = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (IsSqlServer(databaseProvider))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is required when Database:Provider is SqlServer.");
        }

        return "Data Source=cynara.db";
    }

    private static async Task<bool> AllRequiredTablesExistAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        DbConnection connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            DbCommand command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DbDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        _ = existingTables.Add(reader.GetString(0));
                    }

                    return RequiredTables.All(existingTables.Contains);
                }
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records all known migrations as applied. Called after
    /// <see cref="DatabaseFacade.EnsureCreatedAsync" /> on a fresh SQL Server
    /// database so subsequent deployments can take the migrator path instead
    /// of trying to recreate tables that already exist.
    /// </summary>
    private static async Task SeedMigrationHistoryAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> applied = await dbContext.Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        IEnumerable<string> all = dbContext.Database.GetMigrations();
        List<string> pending = [.. all.Except(applied, StringComparer.Ordinal)];

        if (pending.Count == 0)
        {
            return;
        }

        DbConnection connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            DbCommand command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    "IF OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL "
                    + "CREATE TABLE [__EFMigrationsHistory] ("
                    + "    [MigrationId] nvarchar(150) NOT NULL, "
                    + "    [ProductVersion] nvarchar(32) NOT NULL, "
                    + "    CONSTRAINT [PK___EFMigrationsHistory] "
                    + "        PRIMARY KEY ([MigrationId]));";
                _ = await command.ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (string migrationId in pending)
                {
                    DbCommand insert = connection.CreateCommand();
                    await using (insert.ConfigureAwait(false))
                    {
                        insert.CommandText =
                            "INSERT INTO [__EFMigrationsHistory] "
                            + "([MigrationId], [ProductVersion]) VALUES (@id, @ver)";
                        _ = insert.Parameters.Add(
                            BuildMigrationHistoryParameter(
                                "@id",
                                System.Data.SqlDbType.NVarChar,
                                size: 150,
                                value: migrationId));
                        _ = insert.Parameters.Add(
                            BuildMigrationHistoryParameter(
                                "@ver",
                                System.Data.SqlDbType.NVarChar,
                                size: 32,
                                value: "10.0.10"));
                        _ = await insert.ExecuteNonQueryAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private static Microsoft.Data.SqlClient.SqlParameter BuildMigrationHistoryParameter(
        string name,
        System.Data.SqlDbType sqlType,
        int size,
        object value)
    {
        return new Microsoft.Data.SqlClient.SqlParameter(name, sqlType, size)
        {
            Value = value,
        };
    }
}
