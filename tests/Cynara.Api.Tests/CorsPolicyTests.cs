namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class CorsPolicyTests : IDisposable
{
    public CorsPolicyTests(PostgreSqlDatabaseFixture database)
    {
        DatabaseSettings = database.Settings;
        Factory = new CynaraWebApplicationFactory(DatabaseSettings);
        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Preflight_UnconfiguredOrigin_IsDenied()
    {
        using HttpRequestMessage request = BuildPreflight("http://evil.example");
        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.False(
            response.Headers.TryGetValues(
                "Access-Control-Allow-Origin",
                out _),
            "Unconfigured origin must not be granted CORS access.");
    }

    [Fact]
    public async Task Preflight_ConfiguredOrigin_IsAllowed()
    {
        using HttpRequestMessage request = BuildPreflight("http://localhost:5173");
        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.True(
            response.Headers.TryGetValues(
                "Access-Control-Allow-Origin",
                out IEnumerable<string>? values),
            "Configured origin must be granted CORS access.");
        Assert.Equal(
            "http://localhost:5173",
            Assert.Single(values ?? []));
    }

    [Fact]
    public async Task SimpleRequest_ConfiguredOrigin_CarriesAllowOriginHeader()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/health", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(
            "Origin",
            "http://localhost:5173");

        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.True(
            response.Headers.TryGetValues(
                "Access-Control-Allow-Origin",
                out IEnumerable<string>? values),
            "A configured origin must receive the CORS allow-origin header.");
        Assert.Equal(
            "http://localhost:5173",
            Assert.Single(values ?? []));
    }

    [Fact]
    public async Task Preflight_PreviewWorkersOrigin_IsAllowedByPattern()
    {
        using HttpRequestMessage request = BuildPreflight(
            "https://e7d47474-cynara-web.livesanty.workers.dev");
        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.True(
            response.Headers.TryGetValues(
                "Access-Control-Allow-Origin",
                out IEnumerable<string>? values),
            "Preview Workers origins matching the account pattern must be allowed.");
        Assert.Equal(
            "https://e7d47474-cynara-web.livesanty.workers.dev",
            Assert.Single(values ?? []));
    }

    [Fact]
    public async Task Preflight_ArbitraryWorkerNameUnderAccount_IsAllowed()
    {
        using HttpRequestMessage request = BuildPreflight(
            "https://pr-42-fix-login-cynara-web.livesanty.workers.dev");
        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.True(
            response.Headers.TryGetValues(
                "Access-Control-Allow-Origin",
                out IEnumerable<string>? values),
            "Any worker name under the trusted account subdomain must be allowed.");
        Assert.Equal(
            "https://pr-42-fix-login-cynara-web.livesanty.workers.dev",
            Assert.Single(values ?? []));
    }

    [Fact]
    public async Task Preflight_WorkersOriginOutsideAccount_IsDenied()
    {
        using HttpRequestMessage request = BuildPreflight(
            "https://anything.other-account.workers.dev");
        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.False(
            response.Headers.TryGetValues(
                "Access-Control-Allow-Origin",
                out _),
            "Workers origins outside the configured account must stay denied.");
    }

    private static HttpRequestMessage BuildPreflight(string origin)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Options,
            new Uri("/api/formDefinitions", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation(
            "Access-Control-Request-Method",
            "GET");
        return request;
    }

    private HttpClient Client { get; }

    private TestDatabaseSettings DatabaseSettings { get; }

    private CynaraWebApplicationFactory Factory { get; }
}
