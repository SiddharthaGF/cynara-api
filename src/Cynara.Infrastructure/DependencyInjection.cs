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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cynara.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCynaraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var resolver = new DatabaseConnectionStringResolver(configuration);
        _ = services.AddSingleton(resolver);

        return services.AddCynaraInfrastructure(
            resolver.Resolve(),
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

    public static bool IsPreviewStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Render sets `IS_PULL_REQUEST=true` automatically on every PR preview
        // instance and leaves it unset on the main service. Reading that env
        // var through configuration lets the same image detect whether it is
        // running as a preview without needing a separate configuration knob
        // in the Render dashboard.
        string? value = configuration["IS_PULL_REQUEST"];
        return bool.TryParse(value, out bool isPreview) && isPreview;
    }

    public static IServiceCollection AddCynaraDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        _ = services.AddDbContext<CynaraDbContext>(options =>
            _ = options.UseNpgsql(connectionString));

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
        IConfiguration configuration = services.GetRequiredService<IConfiguration>();
        if (IsPreviewStorage(configuration))
        {
            // Neon PR preview branches inherit the schema from the parent
            // branch; EnsureCreatedAsync would be a noisy no-op on every
            // restart and may also interact badly with concurrent boot
            // attempts while Render is still warming the container.
            return;
        }

        // Neon suspends a compute endpoint after roughly five minutes of
        // inactivity. The first connection after a wake-up can complete the
        // TCP handshake and then receive an EOF mid-protocol while the
        // server is still starting up. Retry the migration a few times
        // before giving up so a cold start does not crash the boot.
        const int maxAttempts = 5;
        var backoff = TimeSpan.FromSeconds(3);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            AsyncServiceScope scope = services.CreateAsyncScope();
            try
            {
                CynaraDbContext dbContext = scope.ServiceProvider
                    .GetRequiredService<CynaraDbContext>();
                _ = await dbContext.Database.EnsureCreatedAsync(cancellationToken)
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
                logger.LogWarning(
                    ex,
                    "Database initialization failed on attempt {Attempt}/{Max}; retrying in {Backoff}s.",
                    attempt,
                    maxAttempts,
                    backoff.TotalSeconds);
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
