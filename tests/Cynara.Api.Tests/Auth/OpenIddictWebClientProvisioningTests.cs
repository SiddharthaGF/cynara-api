using Cynara.Api.Tests.Support;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using OpenIddict.EntityFrameworkCore.Models;

namespace Cynara.Api.Tests.Auth;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class OpenIddictWebClientProvisioningTests : IDisposable
{
    public OpenIddictWebClientProvisioningTests(
        PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraWebApplicationFactory(
            database.Settings,
            new CynaraWebApplicationFactoryOptions
            {
                ExtraConfiguration = new Dictionary<string, string?>(
                    StringComparer.Ordinal)
                {
                    ["OpenIddict:WebClient:RedirectOrigins:0"] =
                        "https://pr-7-cynara-web.livesanty.workers.dev",
                    ["OpenIddict:WebClient:Secret"] = "test-web-secret",
                },
            });
    }

    /// <summary>
    /// The provisioner must merge configured preview origins into the
    /// cynara-web registration so Cloudflare Workers previews can complete
    /// the authorization-code flow without ID2043 redirect rejections.
    /// </summary>
    [Fact]
    public async Task Provision_MergesConfiguredPreviewOriginIntoWebClient()
    {
        _ = Factory.CreateClient();

        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .ProvisionOpenIddictWebClientAsync()
            .ConfigureAwait(false);

        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        OpenIddictEntityFrameworkCoreApplication? client = await identity
            .Set<OpenIddictEntityFrameworkCoreApplication>()
            .SingleOrDefaultAsync(
                item => item.ClientId == "cynara-web")
            .ConfigureAwait(false);

        Assert.NotNull(client);
        Assert.Contains(
            "https://pr-7-cynara-web.livesanty.workers.dev/en/login",
            client.RedirectUris,
            StringComparison.Ordinal);
        Assert.Contains(
            "https://pr-7-cynara-web.livesanty.workers.dev/es/login",
            client.RedirectUris,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private CynaraWebApplicationFactory Factory { get; }
}
