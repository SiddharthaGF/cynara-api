using Cynara.Application.Failures;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Invitations;
using Cynara.Application.Modules.Invitations.Persistence;
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
using Cynara.Infrastructure.Modules.Invitations;
using Cynara.Infrastructure.Modules.Patients;
using Cynara.Infrastructure.Modules.Tasks;
using Cynara.Infrastructure.Modules.Workflows;
using Cynara.Infrastructure.Persistence.QueryCounting;
using Cynara.Infrastructure.Schemas;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cynara.Infrastructure;

public static partial class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Idempotent DDL for baselining databases predating the capabilities
    /// feature; every statement is guarded so older baselines converge onto
    /// the current schema. Keep aligned with the latest migration.
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

    /// <summary>
    /// Registers the domain and identity database contexts over Npgsql.
    /// The identity context keeps a dedicated migrations history table so
    /// authentication schema can never collide with the domain migration
    /// stamps; cross-context readers resolve against both contexts.
    /// </summary>
    public static IServiceCollection AddCynaraDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        string normalizedConnectionString = PostgreSqlConnectionStringNormalizer
            .Normalize(connectionString);

        _ = services.AddScoped<QueryCounter>();
        _ = services.AddScoped<QueryCountingInterceptor>();
        _ = services.AddDbContext<CynaraDbContext>((provider, options) =>
        {
            _ = options.UseNpgsql(normalizedConnectionString);
            _ = options.AddInterceptors(
                provider.GetRequiredService<QueryCountingInterceptor>());
        });

        _ = services.AddDbContext<CynaraIdentityDbContext>(options =>
            _ = options.UseNpgsql(
                normalizedConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    CynaraIdentityDbContext.MigrationsHistoryTableName)));

        _ = services.AddScoped<IUnitOfWork>(
            provider => provider.GetRequiredService<CynaraDbContext>());

        _ = services.AddScoped<
            IHospitalMembershipReader,
            MembershipHospitalReader>();

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
        _ = services.AddScoped<IInvitationRepository, InvitationRepository>();
        _ = services.AddScoped<
            IInvitationExpiryEvaluator,
            InvitationExpiryEvaluator>();
        _ = services.AddSingleton<IInvitationNotifier>(
            provider => new DevelopmentInvitationNotifier(
                provider.GetRequiredService<
                    ILogger<DevelopmentInvitationNotifier>>(),
                provider.GetRequiredService<IHostEnvironment>()));
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
                await EnsureMigratedAsync(
                        dbContext,
                        provisionCapabilityAssignments: true,
                        cancellationToken).ConfigureAwait(false);

                CynaraIdentityDbContext identityDbContext = scope
                    .ServiceProvider
                    .GetRequiredService<CynaraIdentityDbContext>();
                await EnsureMigratedAsync(
                        identityDbContext,
                        provisionCapabilityAssignments: false,
                        cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Migrates a context's database. Databases that already contain the
    /// context's own tables but no migration stamps from the current
    /// assembly are baselined first — this covers EnsureCreated-era
    /// schemas without a history table and histories left stale by
    /// migration squashing — so <c>MigrateAsync</c> never recreates
    /// existing tables. Ownership is decided per context so domain and
    /// identity tracks sharing one database do not mask each other.
    /// </summary>
    private static async Task EnsureMigratedAsync(
        DbContext dbContext,
        bool provisionCapabilityAssignments,
        CancellationToken cancellationToken)
    {
        IRelationalDatabaseCreator creator = dbContext.Database
            .GetService<IRelationalDatabaseCreator>();
        IHistoryRepository history = dbContext.Database
            .GetService<IHistoryRepository>();
        IMigrationsAssembly migrationsAssembly = dbContext.Database
            .GetService<IMigrationsAssembly>();

        bool databaseExists = await creator
            .ExistsAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<string> existingTables = databaseExists
            ? await ListPublicTablesAsync(dbContext, cancellationToken)
                .ConfigureAwait(false)
            : [];

        if (existingTables.Count == 0 || !OwnsAnyTable(dbContext, existingTables))
        {
            await dbContext.Database.MigrateAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        bool hasHistory = await history
            .ExistsAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<HistoryRow> applied = hasHistory
            ? await history.GetAppliedMigrationsAsync(cancellationToken)
                .ConfigureAwait(false)
            : [];

        if (!applied.Any(row =>
                migrationsAssembly.Migrations.ContainsKey(row.MigrationId)))
        {
            await BaselineExistingSchemaAsync(
                    dbContext,
                    history,
                    migrationsAssembly.Migrations.Keys.First(),
                    cancellationToken).ConfigureAwait(false);

            if (provisionCapabilityAssignments)
            {
                _ = await dbContext.Database.ExecuteSqlRawAsync(
                    CapabilityAssignmentsTableSql,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.Database.MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ListPublicTablesAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT tablename AS \"Value\" FROM pg_tables "
                + "WHERE schemaname = 'public'")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool OwnsAnyTable(
        DbContext dbContext,
        IReadOnlyList<string> existingTables)
    {
        var ownTables = dbContext.Model
            .GetRelationalModel()
            .Tables
            .Select(table => table.Name)
            .ToHashSet(StringComparer.Ordinal);
        return existingTables.Any(ownTables.Contains);
    }

    /// <summary>
    /// Stamps the given initial migration into the context's migration
    /// history table so the pre-existing schema is treated as applied.
    /// </summary>
    private static async Task BaselineExistingSchemaAsync(
        DbContext dbContext,
        IHistoryRepository history,
        string initialMigrationId,
        CancellationToken cancellationToken)
    {
        _ = await history.CreateIfNotExistsAsync(cancellationToken)
            .ConfigureAwait(false);

        string productVersion = typeof(DbContext).Assembly
            .GetName()
            .Version!
            .ToString();
        string insertScript = history.GetInsertScript(
            new HistoryRow(initialMigrationId, productVersion));
        _ = await dbContext.Database.ExecuteSqlRawAsync(
            insertScript,
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
