using Cynara.Application;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Hospitals;
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
            string connectionString = ResolveConnectionString(configuration);

            var services = new ServiceCollection();
            _ = services.AddSingleton(configuration);
            _ = services.AddCynaraApplication();
            _ = services.AddCynaraInfrastructure(
                connectionString,
                SchemaFilePaths.FromBaseDirectory());
            _ = services.AddSingleton(TimeProvider.System);

            ServiceProvider serviceProvider = services.BuildServiceProvider();
            try
            {
                await serviceProvider.InitializeDatabaseAsync()
                    .ConfigureAwait(false);
                HospitalBootstrapOptions hospitalOptions = ResolveHospitalOptions(configuration);
                await serviceProvider
                    .EnsureBootstrapHospitalAsync(hospitalOptions)
                    .ConfigureAwait(false);
                await serviceProvider.SeedDemoShowcaseAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                await serviceProvider.DisposeAsync().ConfigureAwait(false);
            }

            Console.WriteLine($"→ Seeded '{DemoShowcaseSeeder.ComponentCode}' "
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
                    ["--hospital-code"] = "Hospitals:BootstrapCode",
                    ["--hospital-name"] = "Hospitals:BootstrapName",
                })
            .Build();
    }

    private static HospitalBootstrapOptions ResolveHospitalOptions(
        IConfiguration configuration)
    {
        HospitalBootstrapOptions options = new();
        configuration
            .GetSection(HospitalBootstrapOptions.SectionName)
            .Bind(options);
        return options;
    }

    private static string ResolveConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Pass --connection or "
                + "ConnectionStrings__Default.");
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
