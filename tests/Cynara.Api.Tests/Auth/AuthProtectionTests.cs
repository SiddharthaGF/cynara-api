using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Verifies protected endpoints reject absent or invalid authentication with
/// 401 and that production attribution can never be spoofed by a forged
/// <c>X-Actor-Id</c> header. These are real-authentication suites: they
/// disable the header seam and exercise genuine OpenIddict tokens.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class AuthProtectionTests : IDisposable
{
    public AuthProtectionTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new IdentityAuthWebApplicationFactory(Database);
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            Factory.BootstrapOptions.BootstrapCode ?? "default");
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private IdentityAuthWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithInvalidToken_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "not-a-real-token");

        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForgedActorHeader_IsIgnoredForAttribution()
    {
        const string email = "forged@cynara.dev";
        const string password = "Cynara!Dev123";
        const string realActor = "real-actor";

        var user = await Factory.CreateUserAsync(email, password);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary workspace"))
            .Id;
        await Factory.SeedMembershipAsync(user, hospitalId, realActor);
        await Factory.RegisterClientAsync();

        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(
            email,
            password);

        Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            tokens.AccessToken);

        Client.DefaultRequestHeaders.TryAddWithoutValidation("X-Actor-Id", "forged-actor");

        using HttpResponseMessage response = await Client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        string actorId = document.RootElement.GetProperty("actorId").GetString()
            ?? string.Empty;

        Assert.Equal(realActor, actorId);
    }

    [Fact]
    public async Task CredentialEndpoint_RateLimitsByIpAndReturnsRetryAfter()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using HttpResponseMessage response = await Client
                .PostAsJsonAsync(
                    "/connect/account/recovery",
                    new { account = "rate-limit@cynara.dev" })
                .ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using HttpResponseMessage rejected = await Client
            .PostAsJsonAsync(
                "/connect/account/recovery",
                new { account = "rate-limit@cynara.dev" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out _));
    }
}
