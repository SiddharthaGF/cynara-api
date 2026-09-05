using System.Net;
using System.Text.Json;

using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Verifies the interactive authorization contract end to end in every
/// environment: authorization-code + PKCE hands the browser to the registered
/// frontend in Development and Production, only opaque request data crosses
/// that handoff, a consumed authorization transaction cannot mint a second
/// code, and bad credentials return to the frontend without issuing a code.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class AuthorizeFlowTests : IDisposable
{
    private const string UserEmail = "authorize@cynara.dev";
    private const string UserPassword = "Cynara!Dev123";

    public AuthorizeFlowTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new IdentityAuthWebApplicationFactory(Database);
        ProductionCertificates = TestOpenIddictCertificates.Create();
        FactoryProduction = new IdentityAuthWebApplicationFactory(
            Database,
            environment: "Production",
            openIddictCertificates: ProductionCertificates);
    }

    public void Dispose()
    {
        Factory.Dispose();
        FactoryProduction.Dispose();
        ProductionCertificates.Dispose();
        GC.SuppressFinalize(this);
    }

    private IdentityAuthWebApplicationFactory Factory { get; }

    private IdentityAuthWebApplicationFactory FactoryProduction { get; }

    private TestOpenIddictCertificates ProductionCertificates { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task AuthorizationCodeWithPkce_WorksInDevelopment()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "authorize-actor");
        await Factory.RegisterClientAsync();

        AuthTokenResult tokens = await Factory.GetAuthorizationCodeTokenAsync(
            UserEmail,
            UserPassword);

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal("Bearer", tokens.AccessTokenType);
    }

    [Fact]
    public async Task AuthorizationCodeWithPkce_WorksInProduction()
    {
        await FactoryProduction.ResetDatabaseAsync();

        var user = await FactoryProduction.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await FactoryProduction.EnsureHospitalAsync(
                FactoryProduction.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await FactoryProduction.SeedMembershipAsync(user, hospitalId, "authorize-actor");
        await FactoryProduction.RegisterClientAsync();

        const string state = "production-state-42";
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        AuthorizePage page = await GetAuthorizePageAsync(
            FactoryProduction,
            state,
            verifier);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);

        using HttpResponseMessage response = await PostCredentialsAsync(
            FactoryProduction,
            page,
            UserEmail,
            UserPassword);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal(state, QueryValue(location, "state"));
        string code = QueryValue(location, "code")
            ?? throw new InvalidOperationException(
                "Authorize redirect missing code.");

        AuthTokenResult tokens = await ExchangeCodeAsync(
            FactoryProduction,
            code,
            verifier);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Fact]
    public async Task PasswordGrant_RemainsClosedInProduction()
    {
        await FactoryProduction.ResetDatabaseAsync();
        _ = await FactoryProduction.CreateUserAsync(UserEmail, UserPassword);
        await FactoryProduction.RegisterClientAsync();

        using HttpClient client = FactoryProduction.CreateClient();
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "password",
                ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
                ["client_secret"] = IdentityAuthWebApplicationFactory.ClientSecret,
                ["username"] = UserEmail,
                ["password"] = UserPassword,
                ["scope"] = "openid profile email offline_access cynara_api",
            });

        using HttpResponseMessage response = await client
            .PostAsync("/connect/token", content)
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationGet_HandsOffOnlyOpaqueRequestData()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "authorize-actor");
        await Factory.RegisterClientAsync();

        const string state = "decoded-state-value-42";
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        string challenge = TokenFlowHelper.CreatePkceChallenge(verifier);

        AuthorizePage page = await GetAuthorizePageAsync(Factory, state, verifier);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal(
            IdentityAuthWebApplicationFactory.RedirectUri,
            page.CallbackUri?.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            IdentityAuthWebApplicationFactory.ClientId,
            page.HiddenFields["client_id"]);
        Assert.True(page.HiddenFields.ContainsKey("request_uri"));
        Assert.DoesNotContain("<form", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(state, page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(challenge, page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(state, page.CallbackUri?.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(challenge, page.CallbackUri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizationHandoff_DoesNotExposeDecodedProtocolValues()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "authorize-actor");
        await Factory.RegisterClientAsync();

        AuthorizePage page = await GetAuthorizePageAsync(
            Factory,
            "semantic-state-that-must-not-leak",
            TokenFlowHelper.CreatePkceVerifier());

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.DoesNotContain(
            "semantic-state-that-must-not-leak",
            page.CallbackUri?.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizationTransaction_ReplayedAfterUse_DoesNotMintAnotherCode()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "authorize-actor");
        await Factory.RegisterClientAsync();

        const string state = "replay-state-9";
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        AuthorizePage page = await GetAuthorizePageAsync(Factory, state, verifier);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);

        using HttpResponseMessage first = await PostCredentialsAsync(
            Factory,
            page,
            UserEmail,
            UserPassword);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.True(IsRedirectWithCode(first));

        using HttpResponseMessage replay = await PostCredentialsAsync(
            Factory,
            page,
            UserEmail,
            UserPassword);
        Assert.False(
            IsRedirectWithCode(replay),
            "A replayed authorization submission must not mint another code.");
    }

    [Fact]
    public async Task RepeatedInvalidPassword_StaysOnLogin_DoesNotIssueCode()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "authorize-actor");
        await Factory.RegisterClientAsync();

        const string state = "loop-state-7";
        string verifier = TokenFlowHelper.CreatePkceVerifier();
        AuthorizePage page = await GetAuthorizePageAsync(Factory, state, verifier);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpResponseMessage response = await PostCredentialsAsync(
                Factory,
                page,
                UserEmail,
                "wrong-password");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Uri location = Assert.IsType<Uri>(response.Headers.Location);
            Assert.Equal("invalid_credentials", QueryValue(location, "error"));
            Assert.Equal(
                page.HiddenFields["request_uri"],
                QueryValue(location, "request_uri"));
            Assert.Null(QueryValue(location, "code"));
        }
    }

    [Fact]
    public async Task InteractiveLogin_LocksUserAfterFiveFailures()
    {
        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(UserEmail, UserPassword);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(
                Factory.BootstrapOptions.BootstrapCode ?? "default",
                "Primary")).Id;
        await Factory.SeedMembershipAsync(user, hospitalId, "authorize-actor");
        await Factory.RegisterClientAsync();

        AuthorizePage page = await GetAuthorizePageAsync(
            Factory,
            "lockout-state-5",
            TokenFlowHelper.CreatePkceVerifier());

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage response = await PostCredentialsAsync(
                Factory,
                page,
                UserEmail,
                "wrong-password");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        UserManager<CynaraUser> users = scope.ServiceProvider
            .GetRequiredService<UserManager<CynaraUser>>();
        CynaraUser lockedUser = (await users.FindByNameAsync(UserEmail))!;
        Assert.True(await users.IsLockedOutAsync(lockedUser));

        using HttpResponseMessage validAttempt = await PostCredentialsAsync(
            Factory,
            page,
            UserEmail,
            UserPassword);
        Assert.Equal(HttpStatusCode.Redirect, validAttempt.StatusCode);
        Uri location = Assert.IsType<Uri>(validAttempt.Headers.Location);
        Assert.Equal("invalid_credentials", QueryValue(location, "error"));
        Assert.Null(QueryValue(location, "code"));
    }

    /// <summary>
    /// OpenIddict caches the authorization request and self-redirects the
    /// first interaction to an opaque request_uri transaction; following that
    /// redirect is what surfaces the login form to parse and assert.
    /// </summary>
    private static async Task<AuthorizePage> GetAuthorizePageAsync(
        CynaraWebApplicationFactory factory,
        string state,
        string verifier)
    {
        string challenge = TokenFlowHelper.CreatePkceChallenge(verifier);
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        browser.Timeout = TimeSpan.FromSeconds(120);

        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
            ["redirect_uri"] = IdentityAuthWebApplicationFactory.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile email offline_access cynara_api",
            ["state"] = state,
            ["nonce"] = "authorize-nonce",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };

        string queryString = string.Join(
            '&',
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        using HttpResponseMessage response = await browser
            .GetAsync(new Uri("/connect/authorize?" + queryString, UriKind.Relative))
            .ConfigureAwait(false);

        if (TokenFlowHelper.IsOpaqueTransactionRedirect(response, out _))
        {
            Uri location = response.Headers.Location!;
            using HttpResponseMessage formResponse = await browser
                .GetAsync(location, HttpCompletionOption.ResponseContentRead)
                .ConfigureAwait(false);

            Uri handoff = formResponse.Headers.Location
                ?? throw new InvalidOperationException(
                    "Authorization handoff redirect missing Location: "
                    + formResponse.StatusCode
                    + " "
                    + await formResponse.Content.ReadAsStringAsync()
                        .ConfigureAwait(false));
            return new AuthorizePage(
                formResponse.StatusCode,
                string.Empty,
                ExtractQueryFields(handoff),
                handoff);
        }

        string loginHtml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new AuthorizePage(
            response.StatusCode,
            loginHtml,
            new Dictionary<string, string>(StringComparer.Ordinal),
            response.Headers.Location);
    }

    private static async Task<HttpResponseMessage> PostCredentialsAsync(
        CynaraWebApplicationFactory factory,
        AuthorizePage page,
        string email,
        string password)
    {
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        browser.Timeout = TimeSpan.FromSeconds(120);

        var body = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> field in page.HiddenFields)
        {
            body[field.Key] = field.Value;
        }

        body["email"] = email;
        body["password"] = password;
        using FormUrlEncodedContent content = new(body);
        return await browser.PostAsync("/connect/authorize", content)
            .ConfigureAwait(false);
    }

    private static async Task<AuthTokenResult> ExchangeCodeAsync(
        CynaraWebApplicationFactory factory,
        string code,
        string verifier)
    {
        using HttpClient client = factory.CreateClient();
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
            ["client_secret"] = IdentityAuthWebApplicationFactory.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = IdentityAuthWebApplicationFactory.RedirectUri,
            ["code_verifier"] = verifier,
        };

        using FormUrlEncodedContent content = new(body);
        using HttpResponseMessage response = await client
            .PostAsync("/connect/token", content).ConfigureAwait(false);
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

    private static bool IsRedirectWithCode(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.Redirect
            && response.Headers.Location is not null
            && QueryValue(response.Headers.Location, "code") is not null;
    }

    private static string? QueryValue(Uri uri, string key)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(uri.Query);
        return query.TryGetValue(key, out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }

    private static Dictionary<string, string> ExtractQueryFields(Uri uri)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> pair in
            Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query))
        {
            fields[pair.Key] = pair.Value.ToString();
        }

        return fields;
    }

    private sealed record AuthorizePage(
        HttpStatusCode StatusCode,
        string Html,
        IReadOnlyDictionary<string, string> HiddenFields,
        Uri? CallbackUri = null);
}
