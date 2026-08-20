using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;

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

        // A client-supplied actor header naming a different actor must be
        // ignored in production.
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
}
