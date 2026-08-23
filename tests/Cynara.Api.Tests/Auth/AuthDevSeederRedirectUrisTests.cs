using Cynara.Infrastructure.Modules.Preview;

using Microsoft.Extensions.Configuration;

namespace Cynara.Api.Tests.Auth;

public sealed class AuthDevSeederRedirectUrisTests
{
    [Fact]
    public void Resolve_WithoutConfiguration_ReturnsLocalhostUrisOnly()
    {
        IReadOnlyList<string> uris = AuthDevSeeder.ResolveWebRedirectUris(configuration: null);

        Assert.Equal(4, uris.Count);
        Assert.All(uris, uri => Assert.StartsWith("http://", uri, StringComparison.Ordinal));
        Assert.Contains(AuthDevSeeder.WebRedirectUriEnglish, uris);
        Assert.Contains(AuthDevSeeder.WebRedirectUriSpanish, uris);
    }

    [Fact]
    public void Resolve_WithPreviewOrigin_AppendsLocalizedLoginPaths()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["Preview:WebAppOrigins:0"] =
                    "https://e7d47474-cynara-web.livesanty.workers.dev",
            })
            .Build();

        IReadOnlyList<string> uris =
            AuthDevSeeder.ResolveWebRedirectUris(configuration);

        Assert.Contains(
            "https://e7d47474-cynara-web.livesanty.workers.dev/es/login",
            uris);
        Assert.Contains(
            "https://e7d47474-cynara-web.livesanty.workers.dev/en/login",
            uris);
    }

    [Fact]
    public void Resolve_TrimsTrailingSlash_AndSkipsInvalidEntries()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["Preview:WebAppOrigins:0"] =
                    "https://abc123-cynara-web.livesanty.workers.dev/",
                ["Preview:WebAppOrigins:1"] = "not-a-uri",
                ["Preview:WebAppOrigins:2"] = string.Empty,
            })
            .Build();

        IReadOnlyList<string> uris =
            AuthDevSeeder.ResolveWebRedirectUris(configuration);

        Assert.Equal(6, uris.Count);
        Assert.Contains(
            "https://abc123-cynara-web.livesanty.workers.dev/en/login",
            uris);
    }
}
