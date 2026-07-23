using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Cynara.Api.Tests.Support;

internal class CynaraWebApplicationFactory(TestDatabaseSettings database)
    : WebApplicationFactory<Program>
{
    public CynaraWebApplicationFactory()
        : this(TestDatabaseSettings.SqliteInMemory)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _ = builder.UseEnvironment("Development");
        _ = builder.UseCynaraTestDatabase(database);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Database:Provider"] = database.Provider,
                    ["ConnectionStrings:Default"] = database.ConnectionString,
                });
        });

        return base.CreateHost(builder);
    }
}
