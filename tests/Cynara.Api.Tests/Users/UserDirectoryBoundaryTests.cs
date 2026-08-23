using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Capabilities;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Users;

/// <summary>
/// Boundary matrix for the scoped user directory with real OpenIddict
/// tokens: platform-scope <c>users.read</c> lists globally (multi-hospital
/// users exactly once), hospital-scoped grants see only their resolved
/// hospital, missing grants yield the audited 403 envelope, and
/// client-credentials subjects are denied.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class UserDirectoryBoundaryTests : IDisposable
{
    private const string HospitalACode = "users-a";
    private const string HospitalBCode = "users-b";
    private const string Password = "Cynara!Dev123";

    public UserDirectoryBoundaryTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new IdentityAuthWebApplicationFactory(
            Database,
            grantAllCapabilities: false);
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private IdentityAuthWebApplicationFactory Factory { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task PlatformCaller_ListsGlobally_MultiHospitalUserOnce()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        Guid hospitalB = (await Factory.EnsureHospitalAsync(
            HospitalBCode,
            "Hospital B")).Id;

        IdentityUser<Guid> shared = await SeedMemberAsync(
            "shared@cynara.dev",
            hospitalA,
            actorId: "actor-shared");
        await Factory.SeedMembershipAsync(shared, hospitalB, "actor-shared-b");
        _ = await SeedMemberAsync("only-a@cynara.dev", hospitalA, "actor-only-a");
        _ = await SeedForeignMemberAsync("only-b@cynara.dev", hospitalB);
        IdentityUser<Guid> platformCaller = await Factory.CreateUserAsync(
            "platform@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(
            platformCaller,
            hospitalA,
            "platform-admin");
        await Factory.SeedPlatformCapabilityAsync(
            hospitalA,
            "platform-admin",
            CapabilityCodes.UsersRead);

        HttpClient client = await CreateTokenClientAsync(
            "platform@cynara.dev",
            HospitalACode);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        JsonElement items = document.RootElement.GetProperty("items");
        List<Guid> ids = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            ids.Add(item.GetProperty("id").GetGuid());
        }

        Assert.Equal(4, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(4, ids.Count);
        Assert.Equal(4, ids.Distinct().Count());
        Assert.Contains(shared.Id, ids);
    }

    [Fact]
    public async Task HospitalCaller_SeesOnlyResolvedHospital()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        Guid hospitalB = (await Factory.EnsureHospitalAsync(
            HospitalBCode,
            "Hospital B")).Id;

        IdentityUser<Guid> memberA = await SeedMemberAsync(
            "member-a@cynara.dev",
            hospitalA,
            "actor-member-a");
        _ = await SeedForeignMemberAsync("member-b@cynara.dev", hospitalB);
        IdentityUser<Guid> caller = await Factory.CreateUserAsync(
            "hospital-admin@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(caller, hospitalA, "hosp-admin");
        await Factory.SeedCapabilityAsync(
            hospitalA,
            "hosp-admin",
            CapabilityCodes.UsersRead);

        HttpClient client = await CreateTokenClientAsync(
            "hospital-admin@cynara.dev",
            HospitalACode);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        JsonElement items = document.RootElement.GetProperty("items");

        Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, items.GetArrayLength());
        List<Guid> ids = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            ids.Add(item.GetProperty("id").GetGuid());
        }

        Assert.Contains(memberA.Id, ids);
    }

    [Fact]
    public async Task MissingUsersRead_Returns403ProblemDetails_WithAudit()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        _ = await SeedMemberAsync("plain@cynara.dev", hospitalA, "actor-plain");
        int deniedBefore = await CountAuditEventsAsync("access.denied");

        HttpClient client = await CreateTokenClientAsync(
            "plain@cynara.dev",
            HospitalACode);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users", UriKind.Relative)).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403, got {response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        JsonElement error = Assert.Single(
            document.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("403", error.GetProperty("status").GetString());
        Assert.Equal(
            "Capability required",
            error.GetProperty("title").GetString());
        Assert.True(
            await CountAuditEventsAsync("access.denied") > deniedBefore,
            "Expected an access.denied audit event.");
    }

    [Fact]
    public async Task ClientCredentialsToken_IsDenied()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        _ = await Factory.EnsureHospitalAsync(HospitalACode, "Hospital A");
        await Factory.RegisterClientAsync();

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            HospitalACode);
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = IdentityAuthWebApplicationFactory.ClientId,
            ["client_secret"] = IdentityAuthWebApplicationFactory.ClientSecret,
            ["scope"] = "cynara_api",
        };
        string accessToken;
        using (var content = new FormUrlEncodedContent(body))
        {
            using HttpResponseMessage tokenResponse = await client
                .PostAsync("/connect/token", content).ConfigureAwait(false);
            tokenResponse.EnsureSuccessStatusCode();
            using var token = JsonDocument.Parse(
                await tokenResponse.Content.ReadAsStringAsync()
                    .ConfigureAwait(false));
            accessToken = token.RootElement.GetProperty("access_token")
                .GetString()
                ?? throw new InvalidOperationException("No access token.");
        }

        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<IdentityUser<Guid>> SeedMemberAsync(
        string email,
        Guid hospitalId,
        string actorId)
    {
        IdentityUser<Guid> user = await Factory.CreateUserAsync(email, Password);
        await Factory.SeedMembershipAsync(user, hospitalId, actorId);
        return user;
    }

    private async Task<IdentityUser<Guid>> SeedForeignMemberAsync(
        string email,
        Guid hospitalId)
    {
        IdentityUser<Guid> user = await Factory.CreateUserAsync(email, Password);
        await Factory.SeedMembershipAsync(user, hospitalId, $"actor-{email}");
        return user;
    }

    private async Task<HttpClient> CreateTokenClientAsync(
        string email,
        string hospitalCode)
    {
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, Password);
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            hospitalCode);
        return client;
    }

    private async Task<int> CountAuditEventsAsync(string action)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .Where(item => item.Action == action)
            .CountAsync()
            .ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }
}
