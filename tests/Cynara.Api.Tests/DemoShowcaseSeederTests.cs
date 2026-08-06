using Cynara.Api.Tests.Support;
using Cynara.Domain.Capabilities;
using Cynara.Infrastructure.Modules.Preview;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class DemoShowcaseSeederTests : IDisposable
{
    private readonly CynaraWebApplicationFactory factory;
    private readonly HttpClient client;

    public DemoShowcaseSeederTests(PostgreSqlDatabaseFixture database)
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
    public async Task SeedDemoShowcase_RunningTwice_DoesNotDuplicateGrants()
    {
        await factory.Services.SeedDemoShowcaseAsync().ConfigureAwait(false);
        await factory.Services.SeedDemoShowcaseAsync().ConfigureAwait(false);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();

        List<CapabilityAssignment> grants = await dbContext
            .CapabilityAssignments
            .AsNoTracking()
            .Where(item => item.ActorId == "demo-seed")
            .ToListAsync()
            .ConfigureAwait(false);

        Assert.Equal(2, grants.Count);
        Assert.Equal(
            [CapabilityCodes.CatalogRead, CapabilityCodes.CatalogWrite],
            grants.Select(item => item.Capability).Order(StringComparer.Ordinal));
    }
}
