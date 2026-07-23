using Cynara.Application;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Preview;
using Cynara.Infrastructure.Schemas;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Seed;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            IConfiguration configuration = BuildConfiguration(args);
            string provider = ResolveProvider(configuration);
            string connectionString = ResolveConnectionString(configuration, provider);

            var services = new ServiceCollection();
            _ = services.AddSingleton(configuration);
            _ = services.AddCynaraApplication();
            _ = services.AddCynaraInfrastructure(
                connectionString,
                SchemaFilePaths.FromBaseDirectory(),
                provider);
            _ = services.AddSingleton(TimeProvider.System);

            ServiceProvider serviceProvider = services.BuildServiceProvider();
            try
            {
                await serviceProvider.InitializeDatabaseAsync()
                    .ConfigureAwait(false);
                await serviceProvider.SeedDemoShowcaseAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                await serviceProvider.DisposeAsync().ConfigureAwait(false);
            }

            Console.WriteLine($"→ Provider: {provider}");
            Console.WriteLine(
                $"→ Seeded '{DemoShowcaseSeeder.ComponentCode}' "
                + $"and '{DemoShowcaseSeeder.FormCode}'.");
            Console.WriteLine("→ Open: /forms/demo-showcase/designer");
            return 0;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.Message)
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static IConfigurationRoot BuildConfiguration(string[] args)
    {
        string? apiSettingsDir = ResolveApiSettingsDirectory();
        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        if (apiSettingsDir is not null)
        {
            _ = builder
                .AddJsonFile(
                    Path.Combine(apiSettingsDir, "appsettings.json"),
                    optional: true,
                    reloadOnChange: false)
                .AddJsonFile(
                    Path.Combine(
                        apiSettingsDir,
                        $"appsettings.{ResolveEnvironmentName()}.json"),
                    optional: true,
                    reloadOnChange: false);
        }

        return builder
            .AddEnvironmentVariables()
            .AddCommandLine(
                args,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["--connection"] = "ConnectionStrings:Default",
                    ["--provider"] = "Database:Provider",
                })
            .Build();
    }

    private static string ResolveProvider(IConfiguration configuration)
    {
        string? provider = configuration["Database:Provider"];
        return string.IsNullOrWhiteSpace(provider)
            ? InfrastructureServiceCollectionExtensions.SqliteProvider
            : provider.Trim();
    }

    private static string ResolveConnectionString(
        IConfiguration configuration,
        string provider)
    {
        string? configured = configuration.GetConnectionString("Default");
        bool sqlServer = InfrastructureServiceCollectionExtensions.IsSqlServer(provider);

        if (sqlServer)
        {
            if (string.IsNullOrWhiteSpace(configured) || LooksLikeSqlite(configured))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Default must be a SQL Server connection string when "
                    + "Database:Provider is SqlServer. Pass --connection or "
                    + "ConnectionStrings__Default.");
            }

            return configured;
        }

        return string.IsNullOrWhiteSpace(configured)
            ? "Data Source=cynara.db"
            : configured;
    }

    private static bool LooksLikeSqlite(string connectionString)
    {
        return connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains(
                "Initial Catalog=",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveEnvironmentName()
    {
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";
    }

    private static string? ResolveApiSettingsDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "src", "Cynara.Api");
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
}
