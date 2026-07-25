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

        connectionString = NormalizeConnectionString(connectionString);

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

    /// <summary>
    /// Validates a raw connection string and, when given a
    /// <c>postgresql://</c> URI, converts it to the Npgsql key=value form so
    /// the rest of the stack can consume it. Throws with a clear remediation
    /// message when the value cannot be made valid.
    /// </summary>
    private static string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is empty. Set the "
                + "ConnectionStrings__Default environment variable with a "
                + "valid Npgsql connection string (e.g. "
                + "'Host=db;Port=5432;Database=cynara;Username=cynara;Password=...') "
                + "or a postgresql:// URI.");
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
            // Npgsql 10 does not parse postgresql:// URIs natively; convert
            // to the key=value form so the rest of the stack works.
            try
            {
                string converted = ConvertPostgresUriToKeyValue(connectionString);
                _ = new Npgsql.NpgsqlConnectionStringBuilder(converted);
                return converted;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Default is a postgresql:// URI but "
                    + "could not be converted to an Npgsql key=value "
                    + "connection string. Make sure the host is the full "
                    + "Render domain (e.g. dpg-xxx-a.oregon-postgres.render."
                    + "internal:5432) and that the URI includes the database "
                    + "name. Conversion error: " + ex.Message,
                    ex);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Default URI parsed but produced an "
                    + "invalid Npgsql key=value string: " + ex.Message,
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

        return connectionString;
    }

    private static string ConvertPostgresUriToKeyValue(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
        {
            throw new FormatException(
                "Not a valid absolute URI. Use the form "
                + "'postgresql://user:password@host:port/database?...'.");
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder();

        if (!string.IsNullOrEmpty(parsed.Host))
        {
            builder.Host = parsed.Host;
        }

        if (parsed.Port > 0)
        {
            builder.Port = parsed.Port;
        }

        string[] pathSegments = parsed.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length > 0)
        {
            builder.Database = Uri.UnescapeDataString(pathSegments[0]);
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            string[] userInfo = parsed.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
            if (userInfo.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(userInfo[1]);
            }
        }

        if (!string.IsNullOrEmpty(parsed.Query))
        {
            string query = parsed.Query.StartsWith('?')
                ? parsed.Query[1..]
                : parsed.Query;
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = pair.Split('=', 2);
                string key = Uri.UnescapeDataString(kv[0]);
                string value = kv.Length == 2
                    ? Uri.UnescapeDataString(kv[1])
                    : string.Empty;
                builder[key] = value;
            }
        }

        return builder.ConnectionString;
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
