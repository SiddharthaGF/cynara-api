using System.Globalization;

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

        char firstChar = connectionString[0];
        if (char.IsWhiteSpace(firstChar)
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

        bool startsWithUri = connectionString.StartsWith(
            "postgresql://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith(
                "postgres://", StringComparison.OrdinalIgnoreCase);

        if (startsWithUri)
        {
            try
            {
                _ = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
                return;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Default is a postgresql:// URI but "
                    + "Npgsql could not parse it. The URI host may be missing "
                    + "its full Render domain (try the .render.internal or "
                    + ".render.com form). Original parser error: "
                    + ex.Message,
                    ex);
            }
        }

        if (!char.IsLetter(firstChar) || firstChar == '=')
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default does not start with a valid "
                + "key=value pair. The first character is '"
                + DescribeChar(firstChar) + "' (code 0x"
                + ((int)firstChar).ToString("X4", CultureInfo.InvariantCulture)
                + "). The expected format is "
                + "'Host=...;Port=5432;Database=...;Username=...;Password=...' "
                + "or the postgresql:// URI form.");
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

    private static string DescribeChar(char c)
    {
        return c switch
        {
            '\uFEFF' => "BOM (UTF-8 byte order mark)",
            '\u200B' => "zero-width space",
            '\u200C' => "zero-width non-joiner",
            '\u200D' => "zero-width joiner",
            '\u00A0' => "non-breaking space",
            '"' => "double quote",
            '\'' => "single quote",
            '`' => "backtick",
            _ when char.IsControl(c) => "control character",
            _ when char.IsWhiteSpace(c) => "whitespace",
            _ => c.ToString(),
        };
    }
}
