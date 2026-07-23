using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Cynara.Api.Tests.Support;

internal class CynaraWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly TestDatabaseSettings database;

    public CynaraWebApplicationFactory()
        : this(TestDatabaseSettings.SqliteInMemory)
    {
    }

    public CynaraWebApplicationFactory(TestDatabaseSettings database)
    {
        this.database = database;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
