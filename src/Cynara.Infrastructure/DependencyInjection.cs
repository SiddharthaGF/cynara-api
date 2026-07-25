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

namespace Cynara.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCynaraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required for the PostgreSQL provider.");

        ValidateNpgsqlConnectionString(connectionString);

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

    public static bool IsPreviewStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? value = configuration["Database:PreviewStorage"];
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
        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            CynaraDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<CynaraDbContext>();
            _ = await dbContext.Database.EnsureCreatedAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void ValidateNpgsqlConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is empty. Set the "
                + "ConnectionStrings__Default environment variable with a "
                + "valid Npgsql connection string (e.g. "
                + "'Host=db;Port=5432;Database=cynara;Username=cynara;Password=...').");
        }

        if (char.IsWhiteSpace(connectionString[0])
            || connectionString[^1] == '\n' || connectionString[^1] == '\r')
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default has leading/trailing whitespace or "
                + "a trailing newline. Trim the environment variable value in "
                + "the Render dashboard before saving.");
        }

        if (connectionString.Contains('\n', StringComparison.Ordinal)
            || connectionString.Contains('\r', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default contains an embedded newline. "
                + "Replace literal '\\n' or carriage returns in the "
                + "environment variable with a single-line connection string.");
        }

        try
        {
            _ = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not a valid Npgsql connection "
                + "string. Check the environment variable value; the original "
                + "parser error was: " + ex.Message,
                ex);
        }
    }
}
