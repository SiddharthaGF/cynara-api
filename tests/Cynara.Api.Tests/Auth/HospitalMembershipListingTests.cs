using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Verifies the bearer-only hospital membership listing contract:
/// <c>GET /api/me/hospitals</c> returns exactly the caller's hospital
/// code/name memberships without requiring <c>X-Hospital-Code</c>, resolves
/// no actor, and stays isolated per user.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class HospitalMembershipListingTests : IDisposable
{
    private const string PrimaryCode = "listing-a";
    private const string SecondaryCode = "listing-b";
    private const string ThirdCode = "listing-c";

    public HospitalMembershipListingTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new IdentityAuthWebApplicationFactory(Database);
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private IdentityAuthWebApplicationFactory Factory { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task AuthenticatedCaller_TwoMemberships_ReturnsOnlyCodeAndName()
    {
        const string email = "listing@cynara.dev";
        const string password = "Cynara!Dev123";

        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(email, password);
        Guid primary = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        Guid secondary = (await Factory.EnsureHospitalAsync(SecondaryCode, "Secondary"))
            .Id;
        await Factory.SeedMembershipAsync(user, primary, "doctor-primary");
        await Factory.SeedMembershipAsync(user, secondary, "doctor-secondary");
        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/hospitals", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<HospitalEntry> items = await ReadItemsAsync(response);
        Assert.Equal(2, items.Count);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                PrimaryCode,
                SecondaryCode,
            },
            items.Select(item => item.Code).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(
            "Primary",
            items.Single(item => string.Equals(
                item.Code,
                PrimaryCode,
                StringComparison.Ordinal)).Name);
        Assert.Equal(
            "Secondary",
            items.Single(item => string.Equals(
                item.Code,
                SecondaryCode,
                StringComparison.Ordinal)).Name);
    }

    [Fact]
    public async Task AuthenticatedCaller_NoMemberships_ReturnsEmptyCollection()
    {
        const string email = "isolated@cynara.dev";
        const string password = "Cynara!Dev123";

        await Factory.ResetDatabaseAsync();

        _ = await Factory.CreateUserAsync(email, password);
        _ = await Factory.EnsureHospitalAsync(PrimaryCode, "Primary");
        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/hospitals", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadItemsAsync(response));
    }

    [Fact]
    public async Task AnonymousRequest_WithoutToken_Returns401()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();

        HttpClient client = Factory.CreateClient();

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/hospitals", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Sending the outsider's hospital header must neither leak nor re-scope
    /// the listing to the outsider's hospital.
    /// </summary>
    [Fact]
    public async Task Caller_CannotSeeAnotherUsersMemberships_ByChangingHeader()
    {
        const string ownerEmail = "owner@cynara.dev";
        const string outsiderEmail = "outsider@cynara.dev";
        const string password = "Cynara!Dev123";

        await Factory.ResetDatabaseAsync();

        var owner = await Factory.CreateUserAsync(ownerEmail, password);
        Guid primary = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        Guid secondary = (await Factory.EnsureHospitalAsync(SecondaryCode, "Secondary"))
            .Id;
        await Factory.SeedMembershipAsync(owner, primary, "doctor-primary");
        await Factory.SeedMembershipAsync(owner, secondary, "doctor-secondary");

        var outsider = await Factory.CreateUserAsync(outsiderEmail, password);
        Guid third = (await Factory.EnsureHospitalAsync(ThirdCode, "Third")).Id;
        await Factory.SeedMembershipAsync(outsider, third, "doctor-third");
        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(ownerEmail, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            ThirdCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/hospitals", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<HospitalEntry> items = await ReadItemsAsync(response);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(
            items,
            item => string.Equals(item.Code, ThirdCode, StringComparison.Ordinal));
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                PrimaryCode,
                SecondaryCode,
            },
            items.Select(item => item.Code).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// The listing reads through the active-only reader: a revoked
    /// membership in one hospital is excluded while the active
    /// membership in the other hospital still lists.
    /// </summary>
    [Fact]
    public async Task RevokedMembership_IsExcludedFromListing()
    {
        const string email = "revoked-listing@cynara.dev";
        const string password = "Cynara!Dev123";

        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(email, password);
        Guid primary = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        Guid secondary = (await Factory.EnsureHospitalAsync(SecondaryCode, "Secondary"))
            .Id;
        await Factory.SeedMembershipAsync(user, primary, "doctor-primary");
        await Factory.SeedMembershipAsync(user, secondary, "doctor-secondary");
        await Factory.RevokeMembershipAsync(user.Id, secondary);
        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/hospitals", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<HospitalEntry> items = await ReadItemsAsync(response);
        HospitalEntry single = Assert.Single(items);
        Assert.Equal(PrimaryCode, single.Code);
    }

    /// <summary>
    /// Parses the listing body and asserts every item exposes exactly the
    /// <c>code</c> and <c>name</c> properties and nothing else. A member
    /// leaking <c>id</c>, <c>status</c>, or actor data fails this helper.
    /// </summary>
    private static async Task<IReadOnlyList<HospitalEntry>> ReadItemsAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);

        var items = new List<HospitalEntry>();
        foreach (JsonElement item in root.EnumerateArray())
        {
            Assert.Equal(2, item.EnumerateObject().Count());
            items.Add(new HospitalEntry(
                item.GetProperty("code").GetString() ?? string.Empty,
                item.GetProperty("name").GetString() ?? string.Empty));
        }

        return items;
    }

    private sealed record HospitalEntry(string Code, string Name);
}
