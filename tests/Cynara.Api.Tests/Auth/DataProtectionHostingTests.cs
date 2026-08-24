using Cynara.Api.Hosting;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.DataProtection;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Integration coverage for the DataProtection key ring persisted in the
/// identity database: keys must be written to <c>data_protection_keys</c>
/// and must reload on a fresh provider (a simulated restart), so refresh
/// tokens and authorization artifacts survive deploys and scaled instances.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class DataProtectionHostingTests
{
    public DataProtectionHostingTests(PostgreSqlDatabaseFixture database)
    {
        ConnectionString = database.Settings.ConnectionString;
    }

    private string ConnectionString { get; }

    /// <summary>
    /// A brand-new provider simulates a restarted or scaled instance: the ring
    /// must load back from the identity database instead of regenerating an
    /// ephemeral key set.
    /// </summary>
    [Fact]
    public async Task KeyRing_PersistsToIdentityDatabase_AndSurvivesRestart()
    {
        const string payload = "refresh-token-round-trip";
        string purpose = $"cynara-tests-{Guid.NewGuid():N}";

        string cipherText;
        await using (ServiceProvider original = BuildProvider())
        {
            IDataProtector protector = original
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(purpose);
            cipherText = protector.Protect(payload);

            CynaraIdentityDbContext identity = original
                .GetRequiredService<CynaraIdentityDbContext>();
            Assert.NotEmpty(await identity.DataProtectionKeys.ToListAsync());
        }

        await using ServiceProvider restarted = BuildProvider();
        IDataProtector reloaded = restarted
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(purpose);

        Assert.Equal(payload, reloaded.Unprotect(cipherText));
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddCynaraDatabase(ConnectionString);
        _ = services.AddCynaraDataProtection();
        return services.BuildServiceProvider();
    }
}
