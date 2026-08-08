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
            .Where(item => item.ActorId == "designer-user")
            .ToListAsync()
            .ConfigureAwait(false);

        Assert.Equal(CapabilityCodes.All.Count, grants.Count);
        Assert.Equal(
            CapabilityCodes.All.Order(StringComparer.Ordinal),
            grants.Select(item => item.Capability).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SeedFullDatabase_PopulatesEveryTable()
    {
        await factory.Services.SeedFullDatabaseAsync().ConfigureAwait(false);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();

        Assert.True(await dbContext.Hospitals.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.ComponentDefinitions.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.ComponentVersions.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.FormDefinitions.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.FormVersions.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.FormResponses.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.FormResponseRevisions.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.AuditEvents.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.AiProviderSettings.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.FailureLogs.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.Facilities.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.ClinicalAreas.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.Disciplines.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.DocumentDefinitions.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.ClinicalDocuments.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.Patients.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.Encounters.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.CapabilityAssignments.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.WorkflowDefinitions.AnyAsync().ConfigureAwait(false));
        Assert.True(await dbContext.WorkflowVersions.AnyAsync().ConfigureAwait(false));
    }

    [Fact]
    public async Task SeedFullDatabase_RunningTwice_DoesNotDuplicateRows()
    {
        await factory.Services.SeedFullDatabaseAsync().ConfigureAwait(false);
        await factory.Services.SeedFullDatabaseAsync().ConfigureAwait(false);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();

        Assert.Equal(1, await dbContext.Facilities.CountAsync().ConfigureAwait(false));
        Assert.Equal(1, await dbContext.ClinicalAreas.CountAsync().ConfigureAwait(false));
        Assert.Equal(1, await dbContext.Disciplines.CountAsync().ConfigureAwait(false));
        Assert.Equal(2, await dbContext.Patients.CountAsync().ConfigureAwait(false));
        Assert.Equal(3, await dbContext.Encounters.CountAsync().ConfigureAwait(false));
        Assert.Equal(1, await dbContext.DocumentDefinitions.CountAsync().ConfigureAwait(false));
        Assert.Equal(2, await dbContext.ClinicalDocuments.CountAsync().ConfigureAwait(false));
        Assert.Equal(3, await dbContext.FormResponses.CountAsync().ConfigureAwait(false));
        Assert.Equal(1, await dbContext.AiProviderSettings.CountAsync().ConfigureAwait(false));
        Assert.Equal(1, await dbContext.FailureLogs.CountAsync().ConfigureAwait(false));
        Assert.Equal(1, await dbContext.WorkflowDefinitions.CountAsync().ConfigureAwait(false));
        Assert.Equal(1, await dbContext.WorkflowVersions.CountAsync().ConfigureAwait(false));
        Assert.Equal(
            CapabilityCodes.All.Count,
            await dbContext.CapabilityAssignments.CountAsync().ConfigureAwait(false));
    }
}
