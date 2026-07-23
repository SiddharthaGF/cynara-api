using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Cynara.Api.Tests.Support;

internal static class WebHostBuilderDatabaseExtensions
{
    public static IWebHostBuilder UseCynaraTestDatabase(
        this IWebHostBuilder builder,
        TestDatabaseSettings database)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        _ = builder.UseSetting("Database:Provider", database.Provider);
        _ = builder.UseSetting(
            "ConnectionStrings:Default",
            database.ConnectionString);

        return builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: false,
                reloadOnChange: false);

            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Database:Provider"] = database.Provider,
                    ["ConnectionStrings:Default"] = database.ConnectionString,
                });
        });
    }
}
