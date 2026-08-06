using Cynara.Application.Failures;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Infrastructure.Failures;
using Cynara.Infrastructure.Modules.Audit;
using Cynara.Infrastructure.Modules.Capabilities;
using Cynara.Infrastructure.Modules.ClinicalTaxonomy;
using Cynara.Infrastructure.Modules.Components;
using Cynara.Infrastructure.Modules.Documents;
using Cynara.Infrastructure.Modules.Encounters;
using Cynara.Infrastructure.Modules.FormAi;
using Cynara.Infrastructure.Modules.FormResponses;
using Cynara.Infrastructure.Modules.Forms;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Modules.Patients;
using Cynara.Infrastructure.Persistence;
using Cynara.Infrastructure.Schemas;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cynara.Infrastructure;

public static partial class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Provisioned only when baselining a legacy database that predates the
    /// capabilities feature; keep this DDL aligned with the InitialCreate
    /// migration so future schema changes apply cleanly.
    /// </summary>
    private const string CapabilityAssignmentsTableSql = """
        CREATE TABLE IF NOT EXISTS "capability_assignments" (
            "Id" uuid NOT NULL,
            "HospitalId" uuid NOT NULL,
            "ActorId" character varying(128) NOT NULL,
            "Capability" character varying(64) NOT NULL,
            "AssignedAt" timestamp with time zone NOT NULL,
            "AssignedBy" character varying(128) NULL,
            "RowVersion" bigint NOT NULL,
            CONSTRAINT "PK_capability_assignments" PRIMARY KEY ("Id")
        );

        CREATE INDEX IF NOT EXISTS "IX_capability_assignments_HospitalId"
            ON "capability_assignments" ("HospitalId");

        CREATE UNIQUE INDEX IF NOT EXISTS
            "IX_capability_assignments_HospitalId_ActorId_Capability"
            ON "capability_assignments" ("HospitalId", "ActorId", "Capability");
        """;

    public static IServiceCollection AddCynaraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required for the PostgreSQL provider.");

        return services.AddCynaraInfrastructure(
            connectionString,
            SchemaFilePaths.FromBaseDirectory());
    }

    public static IServiceCollection AddCynaraInfrastructure(
        this IServiceCollection services,
        string connectionString,
        SchemaFilePaths schemaPaths)
    {
        _ = services.AddCynaraDatabase(connectionString);
        _ = services.AddCynaraSchemas(schemaPaths);
        _ = services.AddCynaraPersistence();
        _ = services.AddFormAiInfrastructureModule();
        return services;
    }

    public static IServiceCollection AddCynaraDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        string normalizedConnectionString = PostgreSqlConnectionStringNormalizer
            .Normalize(connectionString);

        _ = services.AddDbContext<CynaraDbContext>(options =>
            _ = options.UseNpgsql(normalizedConnectionString));

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
        _ = services.AddClinicalTaxonomyPersistenceModule();
        _ = services.AddDocumentCatalogPersistenceModule();
        _ = services.AddClinicalDocumentsPersistenceModule();
        _ = services.AddPatientsPersistenceModule();
        _ = services.AddEncountersPersistenceModule();
        _ = services.AddCapabilitiesPersistenceModule();
        _ = services.AddSingleton<IFailureLogWriter, FailureLogWriter>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 5;
        var backoff = TimeSpan.FromSeconds(3);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            AsyncServiceScope scope = services.CreateAsyncScope();
            try
            {
                CynaraDbContext dbContext = scope.ServiceProvider
                    .GetRequiredService<CynaraDbContext>();
                await EnsureDatabaseSchemaAsync(dbContext, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts
                && (ex is Npgsql.NpgsqlException
                    || ex is IOException
                    || ex is TimeoutException))
            {
                ILogger logger = services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Cynara.Infrastructure.DatabaseInitialization");
                LogDatabaseInitRetry(logger, ex, attempt, maxAttempts, backoff.TotalSeconds);
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureDatabaseSchemaAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        IRelationalDatabaseCreator creator = dbContext.Database
            .GetService<IRelationalDatabaseCreator>();
        IHistoryRepository history = dbContext.Database
            .GetService<IHistoryRepository>();

        bool databaseExists = await creator
            .ExistsAsync(cancellationToken)
            .ConfigureAwait(false);
        bool hasTables = databaseExists
            && await creator.HasTablesAsync(cancellationToken).ConfigureAwait(false);
        bool hasHistory = hasTables
            && await history.ExistsAsync(cancellationToken).ConfigureAwait(false);

        if (hasTables && !hasHistory)
        {
            // Databases created before EF migrations existed (EnsureCreated)
            // have tables but no migration history. Baseline them so
            // MigrateAsync never recreates existing tables, and provision the
            // capability_assignments table added after that legacy schema.
            await BaselineLegacyDatabaseAsync(dbContext, history, cancellationToken)
                .ConfigureAwait(false);
        }

        await dbContext.Database.MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task BaselineLegacyDatabaseAsync(
        CynaraDbContext dbContext,
        IHistoryRepository history,
        CancellationToken cancellationToken)
    {
        _ = await history.CreateIfNotExistsAsync(cancellationToken)
            .ConfigureAwait(false);

        string initialMigrationId = dbContext.Database
            .GetService<IMigrationsAssembly>()
            .Migrations
            .Keys
            .First();
        _ = await dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"__EFMigrationsHistory\" " +
            "(\"MigrationId\", \"ProductVersion\") VALUES " +
            "(@p0, '10.0.10') ON CONFLICT DO NOTHING;",
            [initialMigrationId],
            cancellationToken).ConfigureAwait(false);

        // capability_assignments is part of the baselined initial migration,
        // so provision it explicitly for legacy schemas. The DDL is
        // idempotent and a no-op on schemas that already have the table.
        _ = await dbContext.Database.ExecuteSqlRawAsync(
            CapabilityAssignmentsTableSql,
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Database initialization failed on attempt {Attempt}/{Max}; retrying in {Backoff}s.")]
    private static partial void LogDatabaseInitRetry(
        ILogger logger,
        Exception exception,
        int attempt,
        int max,
        double backoff);
}
