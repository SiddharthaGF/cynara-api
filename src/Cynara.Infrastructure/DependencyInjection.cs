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
using Cynara.Infrastructure.Persistence;
using Cynara.Infrastructure.Schemas;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string SqliteProvider = "Sqlite";
    public const string SqlServerProvider = "SqlServer";

    private static readonly string[] RequiredTables =
    [
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
                _ = await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
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
}
