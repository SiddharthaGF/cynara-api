using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// End-to-end coverage of the OpenIddict server exposed by the API host:
/// discovery and JWKS, authorization-code + PKCE, refresh-token rotation,
/// revocation, and client-credentials denial of capability work.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class TokenFlowTests : IDisposable
{
    private const string UserEmail = "flow@cynara.dev";
    private const string UserPassword = "Cynara!Dev123";

    public TokenFlowTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new IdentityAuthWebApplicationFactory(Database);
        FactoryRestricted = new IdentityAuthWebApplicationFactory(
            Database,
            grantAllCapabilities: false);
    }

    public void Dispose()
    {
        Factory.Dispose();
        FactoryRestricted.Dispose();
        GC.SuppressFinalize(this);
    }

    private IdentityAuthWebApplicationFactory Factory { get; }

    private IdentityAuthWebApplicationFactory FactoryRestricted { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task DiscoveryDocument_IsAvailable()
    {
        HttpClient client = Factory.CreateClient();

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("issuer", out _));
        Assert.True(document.RootElement.TryGetProperty("authorization_endpoint", out _));
        Assert.True(document.RootElement.TryGetProperty("token_endpoint", out _));
        Assert.True(document.RootElement.TryGetProperty("jwks_uri", out _));
        string jwksUri = document.RootElement.GetProperty("jwks_uri").GetString()
            ?? throw new InvalidOperationException("jwks_uri missing.");
        Assert.Contains(".well-known", jwksUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Jwks_ReturnsSigningKeys()
    {
        HttpClient client = Factory.CreateClient();

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/.well-known/jwks", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        JsonElement keys = document.RootElement.GetProperty("keys");
        Assert.True(keys.GetArrayLength() > 0);
    }

    [Fact]
    public async Task AuthorizationCodeWithPkce_ReturnsAccessAndRefreshTokens()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "flow-actor");
        await Factory.RegisterClientAsync();

        AuthTokenResult tokens = await Factory.GetAuthorizationCodeTokenAsync(
            UserEmail,
            UserPassword);

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal("Bearer", tokens.AccessTokenType);
    }

    [Fact]
    public async Task RefreshToken_RotatesAndIssuesNewAccessToken()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "flow-actor");
        await Factory.RegisterClientAsync();

        AuthTokenResult initial = await Factory.GetAuthorizationCodeTokenAsync(
            UserEmail,
            UserPassword);
        Assert.False(string.IsNullOrWhiteSpace(initial.RefreshToken));

        AuthTokenResult refreshed = await RefreshAsync(initial.RefreshToken);

        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshed.RefreshToken));
        Assert.NotEqual(initial.RefreshToken, refreshed.RefreshToken);
    }

    [Fact]
    public async Task RevokedAccessToken_IsRejected()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "flow-actor");
        await Factory.RegisterClientAsync();

        AuthTokenResult tokens = await Factory.GetAuthorizationCodeTokenAsync(
            UserEmail,
            UserPassword);

        using HttpResponseMessage revokeResponse = await RevokeAsync(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            Factory.BootstrapOptions.BootstrapCode ?? "default");

        using HttpResponseMessage protectedResponse = await client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    /// <summary>
    /// A client-credentials subject has no user membership, so it resolves to
    /// no actor and is denied capability-protected work.
    /// </summary>
    [Fact]
    public async Task ClientCredentialsToken_CannotPerformCapabilityWork()
    {
        await FactoryRestricted.ResetDatabaseAsync();

        await FactoryRestricted.RegisterClientAsync();
        HttpClient client = FactoryRestricted.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            FactoryRestricted.BootstrapOptions.BootstrapCode ?? "default");

        AuthTokenResult clientToken = await GetClientCredentialsTokenAsync();

        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            clientToken.AccessToken);
        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<AuthTokenResult> RefreshAsync(string refreshToken)
    {
        HttpClient client = Factory.CreateClient();
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
            ["client_secret"] = IdentityAuthWebApplicationFactory.ClientSecret,
            ["refresh_token"] = refreshToken,
        };

        using FormUrlEncodedContent content = new(body);
        using HttpResponseMessage response = await client
            .PostAsync("/connect/token", content).ConfigureAwait(false);
        return await ParseTokenAsync(response);
    }

    private async Task<HttpResponseMessage> RevokeAsync(string accessToken)
    {
        HttpClient client = Factory.CreateClient();
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["token_type_hint"] = "access_token",
            ["token"] = accessToken,
            ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
            ["client_secret"] = IdentityAuthWebApplicationFactory.ClientSecret,
        };

        using FormUrlEncodedContent content = new(body);
        return await client.PostAsync("/connect/revocation", content)
            .ConfigureAwait(false);
    }

    private async Task<AuthTokenResult> GetClientCredentialsTokenAsync()
    {
        HttpClient client = FactoryRestricted.CreateClient();
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
            ["client_secret"] = IdentityAuthWebApplicationFactory.ClientSecret,
            ["scope"] = "cynara_api",
        };

        using FormUrlEncodedContent content = new(body);
        using HttpResponseMessage response = await client
            .PostAsync("/connect/token", content).ConfigureAwait(false);
        return await ParseTokenAsync(response);
    }

    private static async Task<AuthTokenResult> ParseTokenAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string status = ((int)response.StatusCode).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException(
                $"Token endpoint HTTP {status}: {text}");
        }

        using var document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        return new AuthTokenResult(
            AccessToken: root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken: root.TryGetProperty("refresh_token", out JsonElement refresh)
                ? refresh.GetString() ?? string.Empty
                : string.Empty,
            IdToken: string.Empty,
            AccessTokenType: root.GetProperty("token_type").GetString() ?? "Bearer");
    }
}
