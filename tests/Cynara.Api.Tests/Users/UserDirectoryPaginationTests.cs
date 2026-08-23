using System.Globalization;
using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Application.Modules.Users;
using Cynara.Domain.Capabilities;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Users;

/// <summary>
/// Determinism and filters for <c>GET /api/users</c>: walking pages hits
/// every in-scope user exactly once in normalized-email order with a stable
/// distinct-user total, oversized page sizes clamp, mixed-case search matches
/// email and username substrings, and a hospital filter narrows platform
/// results but never widens a hospital-scoped caller's view.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class UserDirectoryPaginationTests : IDisposable
{
    private const string HospitalACode = "page-a";
    private const string HospitalBCode = "page-b";
    private const string Password = "Cynara!Dev123";

    public UserDirectoryPaginationTests(PostgreSqlDatabaseFixture database)
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

    /// <summary>
    /// A multi-membership user must not inflate any page or total, and the
    /// caller's own resolved-hospital membership stays in the distinct-user
    /// count per spec: every member of the scope is listed, admins included.
    /// </summary>
    [Fact]
    public async Task WalkPages_HitsEachUserOnce_InNormalizedEmailOrder()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        Guid hospitalB = (await Factory.EnsureHospitalAsync(
            HospitalBCode,
            "Hospital B")).Id;
        List<string> emails =
        [
            "paged-01@cynara.dev",
            "paged-02@cynara.dev",
            "paged-03@cynara.dev",
            "paged-04@cynara.dev",
            "paged-05@cynara.dev",
            "paged-06@cynara.dev",
            "paged-07@cynara.dev",
        ];
        for (int index = 0; index < emails.Count; index++)
        {
            IdentityUser<Guid> user = await Factory.CreateUserAsync(
                emails[index],
                Password);
            await Factory.SeedMembershipAsync(
                user,
                hospitalA,
                $"actor-{index.ToString(CultureInfo.InvariantCulture)}");
        }

        IdentityUser<Guid> shared = await Factory.CreateUserAsync(
            "shared@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(shared, hospitalA, "actor-shared");
        await Factory.SeedMembershipAsync(shared, hospitalB, "actor-shared-b");

        IdentityUser<Guid> caller = await Factory.CreateUserAsync(
            "walker@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(caller, hospitalA, "actor-walker");
        await Factory.SeedCapabilityAsync(
            hospitalA,
            "actor-walker",
            CapabilityCodes.UsersRead);

        HttpClient client = await CreateTokenClientAsync(
            "walker@cynara.dev",
            HospitalACode);

        List<string> seenEmails = [];
        int firstTotalCount = -1;
        for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            string requestUri = string.Create(
                CultureInfo.InvariantCulture,
                $"/api/users?page={pageNumber}&pageSize=3");
            using HttpResponseMessage response = await client.GetAsync(
                new Uri(requestUri, UriKind.Relative)).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument document = await ReadJsonAsync(response)
                .ConfigureAwait(false);
            Assert.Equal(
                pageNumber,
                document.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(
                3,
                document.RootElement.GetProperty("pageSize").GetInt32());

            if (firstTotalCount < 0)
            {
                firstTotalCount = document.RootElement
                    .GetProperty("totalCount")
                    .GetInt32();
            }

            Assert.Equal(
                firstTotalCount,
                document.RootElement.GetProperty("totalCount").GetInt32());
            foreach (JsonElement item in document.RootElement
                .GetProperty("items").EnumerateArray())
            {
                seenEmails.Add(item.GetProperty("email").GetString() ?? string.Empty);
            }
        }

        Assert.Equal(9, firstTotalCount);
        Assert.Equal(9, seenEmails.Count);
        Assert.Equal(9, seenEmails.Distinct(StringComparer.Ordinal).Count());
        List<string> expectedOrder = [.. emails.Order(StringComparer.Ordinal)];
        expectedOrder.Add("shared@cynara.dev");
        expectedOrder.Add("walker@cynara.dev");
        Assert.Equal(expectedOrder, seenEmails);
    }

    [Fact]
    public async Task OversizedPageSize_ClampsToMaximum()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        IdentityUser<Guid> member = await Factory.CreateUserAsync(
            "single@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(member, hospitalA, "actor-single");
        HttpClient client = await CreateSeededClientAsync(
            "platform@cynara.dev",
            hospitalA,
            "actor-platform",
            platformScope: true);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users?page=0&pageSize=10000", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        Assert.Equal(1, document.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(
            UserDirectoryFieldLimits.MaxPageSize,
            document.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task MixedCaseQuery_MatchesEmailAndUsernameSubstrings()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        IdentityUser<Guid> byEmail = await Factory.CreateUserAsync(
            "searchy.one@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(byEmail, hospitalA, "actor-one");
        IdentityUser<Guid> byUsername = await Factory.CreateUserAsync(
            "unrelated@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(byUsername, hospitalA, "actor-two");
        await using (AsyncServiceScope scope = Factory.Services.CreateAsyncScope())
        {
            UserManager<IdentityUser<Guid>> userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<IdentityUser<Guid>>>();
            IdentityResult renamed = await userManager.SetUserNameAsync(
                byUsername,
                "dr.searchy-42");
            Assert.True(renamed.Succeeded);
        }

        _ = await Factory.CreateUserAsync("bystander@cynara.dev", Password);
        HttpClient client = await CreateSeededClientAsync(
            "admin@cynara.dev",
            hospitalA,
            "actor-admin");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users?q=sEaRcHy", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        JsonElement items = document.RootElement.GetProperty("items");
        Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
        List<string?> matched = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            matched.Add(item.GetProperty("email").GetString());
        }

        List<string?> expectedEmails =
            ["searchy.one@cynara.dev", "unrelated@cynara.dev"];
        Assert.Equal(expectedEmails, matched);
    }

    [Fact]
    public async Task HospitalFilter_NarrowsPlatformResults()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        Guid hospitalB = (await Factory.EnsureHospitalAsync(
            HospitalBCode,
            "Hospital B")).Id;
        IdentityUser<Guid> memberB = await Factory.CreateUserAsync(
            "member-b@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(memberB, hospitalB, "actor-b");
        _ = await Factory.CreateUserAsync("member-a@cynara.dev", Password);
        HttpClient client = await CreateSeededClientAsync(
            "admin@cynara.dev",
            hospitalA,
            "actor-admin",
            platformScope: true);

        using HttpResponseMessage filtered = await client.GetAsync(
            new Uri($"/api/users?hospital={HospitalBCode}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        using JsonDocument document = await ReadJsonAsync(filtered)
            .ConfigureAwait(false);
        Assert.Equal(1, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            memberB.Id,
            document.RootElement.GetProperty("items")[0]
                .GetProperty("id").GetGuid());
    }

    /// <summary>
    /// The caller's own membership stays in view; the foreign hospital filter
    /// never widens the result set beyond the resolved hospital.
    /// </summary>
    [Fact]
    public async Task HospitalFilter_CannotWidenHospitalScope()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        Guid hospitalB = (await Factory.EnsureHospitalAsync(
            HospitalBCode,
            "Hospital B")).Id;
        IdentityUser<Guid> memberA = await Factory.CreateUserAsync(
            "member-a@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(memberA, hospitalA, "actor-a");
        IdentityUser<Guid> memberB = await Factory.CreateUserAsync(
            "member-b@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(memberB, hospitalB, "actor-b");
        HttpClient client = await CreateSeededClientAsync(
            "admin@cynara.dev",
            hospitalA,
            "actor-admin");

        using HttpResponseMessage plain = await client.GetAsync(
            new Uri("/api/users", UriKind.Relative)).ConfigureAwait(false);
        using HttpResponseMessage widened = await client.GetAsync(
            new Uri($"/api/users?hospital={HospitalBCode}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, widened.StatusCode);
        using JsonDocument plainDocument = await ReadJsonAsync(plain)
            .ConfigureAwait(false);
        using JsonDocument widenedDocument = await ReadJsonAsync(widened)
            .ConfigureAwait(false);
        Assert.Equal(
            plainDocument.RootElement.GetProperty("totalCount").GetInt32(),
            widenedDocument.RootElement.GetProperty("totalCount").GetInt32());
        List<Guid> widenedIds = [];
        foreach (JsonElement item in widenedDocument.RootElement
            .GetProperty("items").EnumerateArray())
        {
            widenedIds.Add(item.GetProperty("id").GetGuid());
        }

        Assert.Equal(
            2,
            widenedDocument.RootElement.GetProperty("totalCount").GetInt32());

        Assert.Contains(memberA.Id, widenedIds);
    }

    [Fact]
    public async Task NoMatches_Returns200_WithEmptyItemsAndZeroCount()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        HttpClient client = await CreateSeededClientAsync(
            "admin@cynara.dev",
            hospitalA,
            "actor-admin");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users?q=zzecho-nomatch", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        Assert.Equal(
            0,
            document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            0,
            document.RootElement.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// Seeds an admin caller with a <c>users.read</c> grant in the given
    /// hospital (hospital scope unless <paramref name="platformScope"/>),
    /// mints a real bearer token, and applies the caller's hospital header.
    /// </summary>
    private async Task<HttpClient> CreateSeededClientAsync(
        string email,
        Guid hospitalId,
        string actorId,
        bool platformScope = false)
    {
        IdentityUser<Guid> caller = await Factory.CreateUserAsync(email, Password);
        await Factory.SeedMembershipAsync(caller, hospitalId, actorId);
        if (platformScope)
        {
            await Factory.SeedPlatformCapabilityAsync(
                hospitalId,
                actorId,
                CapabilityCodes.UsersRead);
        }
        else
        {
            await Factory.SeedCapabilityAsync(
                hospitalId,
                actorId,
                CapabilityCodes.UsersRead);
        }

        return await CreateTokenClientAsync(email, HospitalACode);
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

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }
}
