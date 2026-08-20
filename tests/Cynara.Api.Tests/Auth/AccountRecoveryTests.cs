using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Api.Tests.Support;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Verifies anonymous account recovery without account enumeration and the
/// bounded single-use reset contract. These suites exercise the real
/// Identity token providers against the Development host; the token is never
/// returned in the recovery response and reset failures are generic.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class AccountRecoveryTests : IDisposable
{
    private const string RecoveryUrl = "/connect/account/recovery";
    private const string ResetUrl = "/connect/account/reset";
    private const string OldPassword = "Cynara!Dev123";
    private const string NewPassword = "Cynara!New123";

    public AccountRecoveryTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new IdentityAuthWebApplicationFactory(Database);
        Client = Factory.CreateClient();
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
    public async Task RecoveryRequest_KnownAndUnknownAccounts_ReturnByteIdenticalSuccess()
    {
        string knownAccount = "known-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        _ = await Factory.CreateUserAsync(knownAccount, OldPassword).ConfigureAwait(false);
        string unknownAccount = "unknown-" + Guid.NewGuid().ToString("N") + "@cynara.dev";

        string knownBody = await SubmitRecoveryAsync(knownAccount).ConfigureAwait(false);
        string unknownBody = await SubmitRecoveryAsync(unknownAccount).ConfigureAwait(false);

        Assert.Equal(knownBody, unknownBody);
    }

    [Fact]
    public async Task RecoveryRequest_ResponseDoesNotExposeResetToken()
    {
        string account = "token-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        _ = await Factory.CreateUserAsync(account, OldPassword).ConfigureAwait(false);

        using HttpResponseMessage response = await Client
            .PostAsJsonAsync(RecoveryUrl, new { account })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            Assert.False(
                property.Name.Equals("token", StringComparison.OrdinalIgnoreCase),
                $"Response exposed a token field: {property.Name}");
        }
    }

    [Fact]
    public async Task Reset_WithValidToken_RotatesPassword()
    {
        string account = "rotate-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        _ = await Factory.CreateUserAsync(account, OldPassword).ConfigureAwait(false);
        await Factory.RegisterClientAsync().ConfigureAwait(false);
        string token = await GenerateResetTokenAsync(account).ConfigureAwait(false);

        using HttpResponseMessage response = await SubmitResetAsync(
            account,
            token,
            NewPassword).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = await Assert
            .ThrowsAsync<InvalidOperationException>(() => Factory
                .GetPasswordTokenAsync(account, OldPassword))
            .ConfigureAwait(false);
        AuthTokenResult rotated = await Factory
            .GetPasswordTokenAsync(account, NewPassword)
            .ConfigureAwait(false);
        Assert.False(string.IsNullOrEmpty(rotated.AccessToken));
    }

    [Fact]
    public async Task Reset_WithInvalidOrExpiredToken_DoesNotChangePassword()
    {
        string account = "invalid-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        _ = await Factory.CreateUserAsync(account, OldPassword).ConfigureAwait(false);
        await Factory.RegisterClientAsync().ConfigureAwait(false);

        using HttpResponseMessage response = await SubmitResetAsync(
            account,
            "not-a-real-token",
            NewPassword).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AuthTokenResult unchanged = await Factory
            .GetPasswordTokenAsync(account, OldPassword)
            .ConfigureAwait(false);
        Assert.False(string.IsNullOrEmpty(unchanged.AccessToken));
    }

    [Fact]
    public async Task Reset_WithTokenMismatchedToAnotherAccount_DoesNotChangePassword()
    {
        string first = "first-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        string second = "second-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        _ = await Factory.CreateUserAsync(first, OldPassword).ConfigureAwait(false);
        _ = await Factory.CreateUserAsync(second, OldPassword).ConfigureAwait(false);
        await Factory.RegisterClientAsync().ConfigureAwait(false);
        string tokenForFirst = await GenerateResetTokenAsync(first).ConfigureAwait(false);

        using HttpResponseMessage response = await SubmitResetAsync(
            second,
            tokenForFirst,
            NewPassword).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AuthTokenResult unchanged = await Factory
            .GetPasswordTokenAsync(second, OldPassword)
            .ConfigureAwait(false);
        Assert.False(string.IsNullOrEmpty(unchanged.AccessToken));
    }

    [Fact]
    public async Task Reset_ReplayedConsumedToken_DoesNotChangePasswordAgain()
    {
        string account = "replay-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        _ = await Factory.CreateUserAsync(account, OldPassword).ConfigureAwait(false);
        await Factory.RegisterClientAsync().ConfigureAwait(false);
        string token = await GenerateResetTokenAsync(account).ConfigureAwait(false);

        using HttpResponseMessage first = await SubmitResetAsync(
            account,
            token,
            NewPassword).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        const string secondPassword = "Cynara!Again456";
        using HttpResponseMessage replay = await SubmitResetAsync(
            account,
            token,
            secondPassword).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        _ = await Assert
            .ThrowsAsync<InvalidOperationException>(() => Factory
                .GetPasswordTokenAsync(account, secondPassword))
            .ConfigureAwait(false);
        AuthTokenResult firstPassword = await Factory
            .GetPasswordTokenAsync(account, NewPassword)
            .ConfigureAwait(false);
        Assert.False(string.IsNullOrEmpty(firstPassword.AccessToken));
    }

    [Fact]
    public async Task RecoveryAndResetRoutes_AreDocumentedInOpenApi()
    {
        string account = "swagger-" + Guid.NewGuid().ToString("N") + "@cynara.dev";
        _ = await Factory.CreateUserAsync(account, OldPassword).ConfigureAwait(false);
        await Factory.RegisterClientAsync().ConfigureAwait(false);
        AuthTokenResult tokens = await Factory
            .GetPasswordTokenAsync(account, OldPassword)
            .ConfigureAwait(false);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/swagger/v1/swagger.json", UriKind.Relative));
        request.Headers.Authorization = new("Bearer", tokens.AccessToken);
        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        JsonElement paths = document.RootElement.GetProperty("paths");
        Assert.True(
            paths.TryGetProperty("/connect/account/recovery", out _),
            "Recovery route missing from OpenAPI document.");
        Assert.True(
            paths.TryGetProperty("/connect/account/reset", out _),
            "Reset route missing from OpenAPI document.");
    }

    private async Task<string> SubmitRecoveryAsync(string account)
    {
        using HttpResponseMessage response = await Client
            .PostAsJsonAsync(RecoveryUrl, new { account })
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {response.StatusCode.ToString("D")}: {body}");
        return body;
    }

    private Task<HttpResponseMessage> SubmitResetAsync(
        string account,
        string token,
        string newPassword)
    {
        return Client.PostAsJsonAsync(
            ResetUrl,
            new { account, token, newPassword });
    }

    private async Task<string> GenerateResetTokenAsync(string account)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        UserManager<IdentityUser<Guid>> users = scope.ServiceProvider
            .GetRequiredService<UserManager<IdentityUser<Guid>>>();
        IdentityUser<Guid>? user = await users.FindByNameAsync(account)
            .ConfigureAwait(false);
        Assert.NotNull(user);
        return await users.GeneratePasswordResetTokenAsync(user)
            .ConfigureAwait(false);
    }
}
