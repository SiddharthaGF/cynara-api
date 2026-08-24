using Cynara.Api.Hosting;

using Microsoft.Extensions.Configuration;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Verifies the pattern-based redirect URI acceptance: a single anchored
/// regex covers both the production web origin and ephemeral preview
/// origins, structural hygiene always applies (HTTPS, default port, no
/// userinfo/query/fragment/backslash/trailing dot), and an empty or
/// narrower pattern list disables or scopes the allowance.
/// </summary>
public sealed class WebClientRedirectUriPatternPolicyTests
{
    private const string CombinedPattern =
        "^https://(?:[a-z0-9][a-z0-9-]{0,61}-)?cynara-web"
        + @"\.livesanty\.workers\.dev/(?:en|es)/login$";

    [Theory]
    [InlineData("https://cynara-web.livesanty.workers.dev/es/login")]
    [InlineData("https://cynara-web.livesanty.workers.dev/en/login")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev/es/login")]
    [InlineData(
        "https://a1b2c3d4-cynara-web.livesanty.workers.dev/en/login")]
    public void Matches_WithCombinedPattern_AcceptsProdAndPreviews(
        string redirectUri)
    {
        Uri uri = new(redirectUri, UriKind.Absolute);

        Assert.True(CreatePolicy(CombinedPattern).Matches("cynara-web", uri));
    }

    [Theory]
    [InlineData("https://evil.workers.dev/es/login")]
    [InlineData("https://evil.livesanty.workers.dev/es/login")]
    [InlineData("https://evil-cynara.livesanty.workers.dev/es/login")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev.evil.com"
        + "/es/login")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev.evil"
        + "/es/login")]
    [InlineData(
        "http://c37593b2-cynara-web.livesanty.workers.dev/es/login")]
    [InlineData(
        "https://user@c37593b2-cynara-web.livesanty.workers.dev"
        + "/es/login")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev:8443"
        + "/es/login")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev./es/login")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev"
        + "/es/login?next=//evil.com")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev"
        + "/es/login#fragment")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev/callback")]
    [InlineData("https://c37593b2-cynara-web.example.com/es/login")]
    [InlineData("ftp://c37593b2-cynara-web.livesanty.workers.dev/es/login")]
    public void Matches_WithHostileOrForeignUri_Rejects(
        string redirectUri)
    {
        Uri uri = new(redirectUri, UriKind.Absolute);

        Assert.False(CreatePolicy(CombinedPattern).Matches("cynara-web", uri));
    }

    [Fact]
    public void Matches_WithDifferentClientId_Rejects()
    {
        Uri uri = new(
            "https://c37593b2-cynara-web.livesanty.workers.dev/es/login",
            UriKind.Absolute);

        Assert.False(CreatePolicy(CombinedPattern).Matches("other-web", uri));
    }

    [Fact]
    public void Matches_WithEmptyPatterns_RejectsEverything()
    {
        var policy =
            WebClientRedirectUriPatternPolicy.FromConfiguration(
                new ConfigurationBuilder().Build());
        Uri uri = new(
            "https://cynara-web.livesanty.workers.dev/es/login",
            UriKind.Absolute);

        Assert.False(policy.Matches("cynara-web", uri));
    }

    [Fact]
    public void Matches_WithProductionOnlyPattern_RejectsPreviews()
    {
        string productionOnly =
            @"^https://cynara-web\.livesanty\.workers\.dev/(?:en|es)/login$";
        Uri preview = new(
            "https://c37593b2-cynara-web.livesanty.workers.dev/es/login",
            UriKind.Absolute);
        Uri production = new(
            "https://cynara-web.livesanty.workers.dev/es/login",
            UriKind.Absolute);
        var policy = CreatePolicy(productionOnly);

        Assert.True(policy.Matches("cynara-web", production));
        Assert.False(policy.Matches("cynara-web", preview));
    }

    [Fact]
    public void FromConfiguration_WithInvalidRegex_FailsFast()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(
                StringComparer.Ordinal)
            {
                ["OpenIddict:WebClient:RedirectUriPatterns:0"] =
                    "^https://([unclosed",
            })
            .Build();

        _ = Assert.Throws<InvalidOperationException>(
            () => WebClientRedirectUriPatternPolicy.FromConfiguration(
                configuration));
    }

    [Fact]
    public void Matches_WithUppercaseHost_AcceptsCaseInsensitively()
    {
        Uri uri = new(
            "https://C37593B2-CYNARA-WEB.LIVESANTY.WORKERS.DEV/es/login",
            UriKind.Absolute);

        Assert.True(CreatePolicy(CombinedPattern).Matches("cynara-web", uri));
    }

    private static WebClientRedirectUriPatternPolicy CreatePolicy(
        string pattern)
    {
        return WebClientRedirectUriPatternPolicy.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>(
                    StringComparer.Ordinal)
                {
                    ["OpenIddict:WebClient:RedirectUriPatterns:0"] =
                        pattern,
                })
                .Build());
    }
}
