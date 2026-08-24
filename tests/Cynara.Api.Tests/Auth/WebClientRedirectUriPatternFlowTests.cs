using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Verifies the pattern-based redirect URI validation end to end against the
/// real OpenIddict server: a single configured regex accepts both the
/// production origin and ephemeral preview callbacks (completing the full
/// authorization-code + PKCE exchange), while foreign workers, lookalike
/// zones, insecure schemes, and an empty pattern list are rejected.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WebClientRedirectUriPatternFlowTests : IDisposable
{
    private const string UserEmail = "redirect-pattern@cynara.dev";
    private const string UserPassword = "Cynara!Dev123";

    private const string ProductionCallbackUri =
        "https://cynara-web.livesanty.workers.dev/es/login";

    private const string PreviewCallbackUri =
        "https://c37593b2-cynara-web.livesanty.workers.dev/es/login";

    public WebClientRedirectUriPatternFlowTests(
        PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        FactoryWithPatterns = new IdentityAuthWebApplicationFactory(
            Database,
            extraConfiguration: new Dictionary<string, string?>(
                StringComparer.Ordinal)
            {
                ["OpenIddict:WebClient:ClientId"] =
                    IdentityAuthWebApplicationFactory.ClientId,
                ["OpenIddict:WebClient:RedirectUriPatterns:0"] =
                    "^https://(?:[a-z0-9][a-z0-9-]{0,61}-)?cynara-web"
                    + @"\.livesanty\.workers\.dev/(?:en|es)/login$",
            });
        FactoryWithoutPatterns = new IdentityAuthWebApplicationFactory(
            Database);
    }

    public void Dispose()
    {
        FactoryWithPatterns.Dispose();
        FactoryWithoutPatterns.Dispose();
        GC.SuppressFinalize(this);
    }

    private IdentityAuthWebApplicationFactory FactoryWithPatterns { get; }

    private IdentityAuthWebApplicationFactory FactoryWithoutPatterns { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task Authorize_WithProductionRedirectUri_IssuesCode()
    {
        await SeedAsync(FactoryWithPatterns);

        using var browser = CreateBrowser(FactoryWithPatterns);
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        using HttpResponseMessage response = await browser.GetAsync(
            BuildAuthorizeUrl(ProductionCallbackUri, verifier))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(
            TokenFlowHelper.IsOpaqueTransactionRedirect(response, out _));
    }

    [Fact]
    public async Task Authorize_WithPreviewRedirectUri_IssuesCode()
    {
        await SeedAsync(FactoryWithPatterns);

        using var browser = CreateBrowser(FactoryWithPatterns);
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        using HttpResponseMessage response = await browser.GetAsync(
            BuildAuthorizeUrl(PreviewCallbackUri, verifier))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(
            TokenFlowHelper.IsOpaqueTransactionRedirect(response, out _));
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WithPreviewCallback_Completes()
    {
        await SeedAsync(FactoryWithPatterns);

        AuthTokenResult tokens = await TokenFlowHelper
            .GetAuthorizationCodeTokenAsync(
                FactoryWithPatterns,
                IdentityAuthWebApplicationFactory.ClientId,
                IdentityAuthWebApplicationFactory.ClientSecret,
                PreviewCallbackUri,
                UserEmail,
                UserPassword);

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Theory]
    [InlineData("https://evil.workers.dev/es/login")]
    [InlineData(
        "https://c37593b2-cynara-web.livesanty.workers.dev.evil.com"
        + "/es/login")]
    [InlineData("http://c37593b2-cynara-web.livesanty.workers.dev/es/login")]
    public async Task Authorize_WithHostileRedirectUri_IsRejected(
        string redirectUri)
    {
        await SeedAsync(FactoryWithPatterns);

        using var browser = CreateBrowser(FactoryWithPatterns);
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        using HttpResponseMessage response = await browser.GetAsync(
            BuildAuthorizeUrl(redirectUri, verifier))
            .ConfigureAwait(false);

        await AssertInvalidRedirectUriAsync(response);
    }

    [Fact]
    public async Task Authorize_WithoutPatterns_RejectsPreviewUri()
    {
        await SeedAsync(FactoryWithoutPatterns);

        using var browser = CreateBrowser(FactoryWithoutPatterns);
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        using HttpResponseMessage response = await browser.GetAsync(
            BuildAuthorizeUrl(PreviewCallbackUri, verifier))
            .ConfigureAwait(false);

        await AssertInvalidRedirectUriAsync(response);
    }

    private static async Task AssertInvalidRedirectUriAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);

        // The rejection must carry the exact OpenIddict invalid-redirect_uri
        // semantics regardless of the response envelope chosen by the host.
        Assert.Contains("invalid_request", payload, StringComparison.Ordinal);
        Assert.Contains(
            "'redirect_uri' is not valid",
            payload,
            StringComparison.Ordinal);
        Assert.Contains(
            "/errors/ID2043",
            payload,
            StringComparison.Ordinal);
    }

    private static HttpClient CreateBrowser(
        IdentityAuthWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    private static string BuildAuthorizeUrl(
        string redirectUri,
        string verifier)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile email offline_access cynara_api",
            ["state"] = "pattern-state",
            ["nonce"] = "pattern-nonce",
            ["code_challenge"] =
                TokenFlowHelper.CreatePkceChallenge(verifier),
            ["code_challenge_method"] = "S256",
        };

        return "/connect/authorize?" + string.Join(
            '&',
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}="
                + Uri.EscapeDataString(pair.Value)));
    }

    private static async Task SeedAsync(
        IdentityAuthWebApplicationFactory factory)
    {
        await factory.ResetDatabaseAsync().ConfigureAwait(false);
        var user = await factory.CreateUserAsync(UserEmail, UserPassword)
            .ConfigureAwait(false);
        Guid hospitalId = (await factory.EnsureHospitalAsync(
                factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")
            .ConfigureAwait(false)).Id;
        await factory.SeedMembershipAsync(user, hospitalId, "pattern-actor")
            .ConfigureAwait(false);
        await factory.RegisterClientAsync(
            redirectUri: "http://localhost:5173/en/login")
            .ConfigureAwait(false);
    }
}
