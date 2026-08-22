using Cynara.Application.Failures;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Users.Persistence;
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
using Cynara.Infrastructure.Modules.Identity;
using Cynara.Infrastructure.Modules.Patients;
using Cynara.Infrastructure.Modules.Tasks;
using Cynara.Infrastructure.Modules.Workflows;
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
    /// capabilities feature; keep this DDL aligned with the latest migration
    /// so future schema changes apply cleanly. Every statement is guarded:
    /// tables provisioned by earlier baselines predate the Scope column and
    /// the partial unique indexes, and this script must converge them onto
    /// the current schema instead of failing.
    /// </summary>
    private const string CapabilityAssignmentsTableSql = """
        CREATE TABLE IF NOT EXISTS "capability_assignments" (
            "Id" uuid NOT NULL,
            "HospitalId" uuid NOT NULL,
            "ActorId" character varying(128) NOT NULL,
            "Capability" character varying(64) NOT NULL,
            "Scope" character varying(16) NOT NULL DEFAULT 'hospital',
            "AssignedAt" timestamp with time zone NOT NULL,
            "AssignedBy" character varying(128) NULL,
            "RowVersion" bigint NOT NULL,
            CONSTRAINT "PK_capability_assignments" PRIMARY KEY ("Id")
        );

        ALTER TABLE "capability_assignments"
            ADD COLUMN IF NOT EXISTS "Scope"
                character varying(16) NOT NULL DEFAULT 'hospital';

        CREATE INDEX IF NOT EXISTS "IX_capability_assignments_HospitalId"
            ON "capability_assignments" ("HospitalId");

        DROP INDEX IF EXISTS
            "IX_capability_assignments_HospitalId_ActorId_Capability";

        CREATE UNIQUE INDEX IF NOT EXISTS
            "IX_capability_assignments_HospitalId_ActorId_Capability"
            ON "capability_assignments" ("HospitalId", "ActorId", "Capability")
            WHERE "Scope" = 'hospital';

        CREATE UNIQUE INDEX IF NOT EXISTS
            "IX_capability_assignments_ActorId_Capability"
            ON "capability_assignments" ("ActorId", "Capability")
            WHERE "Scope" = 'platform';
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

        // The identity track keeps its own migrations history table so
        // applying authentication schema can never collide with the domain
        // track's migration stamps.
        _ = services.AddDbContext<CynaraIdentityDbContext>(options =>
            _ = options.UseNpgsql(
                normalizedConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    CynaraIdentityDbContext.MigrationsHistoryTableName)));

        _ = services.AddScoped<IUnitOfWork>(
            provider => provider.GetRequiredService<CynaraDbContext>());

        // Membership listing reads both the identity memberships and the
        // domain hospitals, so it registers once both contexts exist.
        _ = services.AddScoped<
            IHospitalMembershipReader,
            MembershipHospitalReader>();

        // The user directory reads users and memberships from the identity
        // context and hospital codes plus capability rows from the domain
        // context, so it registers once both contexts exist as well.
        _ = services.AddScoped<IUserDirectoryReader, UserDirectoryReader>();

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
        _ = services.AddWorkflowsPersistenceModule();
        _ = services.AddTasksPersistenceModule();
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

                CynaraIdentityDbContext identityDbContext = scope
                    .ServiceProvider
                    .GetRequiredService<CynaraIdentityDbContext>();
                await identityDbContext.Database.MigrateAsync(cancellationToken)
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
