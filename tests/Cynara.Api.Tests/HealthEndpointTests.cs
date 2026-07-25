using System.Net;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class HealthEndpointTests : IDisposable
{
    private readonly CynaraWebApplicationFactory factory;
    private readonly HttpClient client;

    public HealthEndpointTests(PostgreSqlDatabaseFixture database)
    {
        factory = new CynaraWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
