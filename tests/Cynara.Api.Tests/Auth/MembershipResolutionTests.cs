using System.Net;
using System.Text.Json;

using Cynara.Domain.Capabilities;

namespace Cynara.Api.Tests.Auth;

/// <summary>
/// Verifies actor resolution is gated by hospital membership: an authenticated
/// user without a matching membership is denied with 403, and the same token
/// resolves to distinct hospital-scoped actors across workspaces. The membership
/// listing endpoint is the single tenant-exempt route: it requires a bearer
/// token but no hospital header, and it must not select an actor or weaken the
/// tenant gates enforced everywhere else.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class MembershipResolutionTests : IDisposable
{
    private const string PrimaryCode = "default";
    private const string SecondaryCode = "hosp-b";

    public MembershipResolutionTests(PostgreSqlDatabaseFixture database)
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
    public async Task AuthenticatedUserWithoutMembership_Returns403()
    {
        const string email = "nomember@cynara.dev";
        const string password = "Cynara!Dev123";

        await Factory.ResetDatabaseAsync();

        _ = await Factory.CreateUserAsync(email, password);
        _ = await Factory.EnsureHospitalAsync(PrimaryCode, "Primary workspace");

        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Hospital-Code", PrimaryCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OneToken_TwoHospitals_ResolvesDistinctActors()
    {
        const string email = "multi@cynara.dev";
        const string password = "Cynara!Dev123";
        const string primaryActor = "doctor-primary";
        const string secondaryActor = "doctor-secondary";

        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(email, password);
        Guid primaryHospital = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        Guid secondaryHospital = (await Factory.EnsureHospitalAsync(SecondaryCode, "Secondary"))
            .Id;
        await Factory.SeedMembershipAsync(user, primaryHospital, primaryActor);
        await Factory.SeedMembershipAsync(user, secondaryHospital, secondaryActor);
        await Factory.RegisterClientAsync();

        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        string actorAtPrimary = await ResolveActorAsync(tokens.AccessToken, PrimaryCode);
        string actorAtSecondary = await ResolveActorAsync(tokens.AccessToken, SecondaryCode);

        Assert.Equal(primaryActor, actorAtPrimary);
        Assert.Equal(secondaryActor, actorAtSecondary);
        Assert.NotEqual(actorAtPrimary, actorAtSecondary);
    }

    /// <summary>
    /// /api/me/* is not wholesale-exempt: only /api/me/hospitals skips the
    /// tenant gate, so a missing hospital header stays a 400 here.
    /// </summary>
    [Fact]
    public async Task TenantRoute_WithoutHospitalHeader_StillRejected()
    {
        const string email = "nohdr@cynara.dev";
        const string password = "Cynara!Dev123";
        const string actor = "doctor-nohdr";

        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(email, password);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        await Factory.SeedMembershipAsync(user, hospitalId, actor);
        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("granted@cynara.dev", "doctor-granted", true, HttpStatusCode.OK)]
    [InlineData("ungranted@cynara.dev", "doctor-ungranted", false, HttpStatusCode.Forbidden)]
    public async Task ListingExemption_DoesNotWeakenCapabilityEnforcement(
        string email, string actor, bool seedCapability, HttpStatusCode expected)
    {
        const string password = "Cynara!Dev123";

        await Factory.ResetDatabaseAsync();

        Guid hospitalId = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        var user = await Factory.CreateUserAsync(email, password);
        await Factory.SeedMembershipAsync(user, hospitalId, actor);
        if (seedCapability)
        {
            await Factory.SeedCapabilityAsync(hospitalId, actor, CapabilityCodes.WorkspaceRead);
        }

        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient listingClient = Factory.CreateClient();
        listingClient.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        using HttpResponseMessage listing = await listingClient
            .GetAsync(new Uri("/api/me/hospitals", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);

        using HttpResponseMessage workspace = await GetWorkspaceAsync(
            tokens.AccessToken);
        Assert.Equal(expected, workspace.StatusCode);
    }

    [Fact]
    public async Task MembershipListing_IsTenantExempt_AndSelectsNoActor()
    {
        const string email = "exempt@cynara.dev";
        const string password = "Cynara!Dev123";
        const string actor = "doctor-exempt";

        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(email, password);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        await Factory.SeedMembershipAsync(user, hospitalId, actor);
        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient listingClient = Factory.CreateClient();
        listingClient.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        using HttpResponseMessage listing = await listingClient
            .GetAsync(new Uri("/api/me/hospitals", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Equal([PrimaryCode], await ReadHospitalCodesAsync(listing));

        string actorAtPrimary = await ResolveActorAsync(tokens.AccessToken, PrimaryCode);
        Assert.Equal(actor, actorAtPrimary);
    }

    /// <summary>
    /// A revoked-only membership must lose resolution: the middleware
    /// delegates to the active-only reader, so the tenant route denies
    /// with 403 exactly like a never-member.
    /// </summary>
    [Fact]
    public async Task RevokedOnlyMembership_Returns403()
    {
        const string email = "revoked@cynara.dev";
        const string password = "Cynara!Dev123";
        const string actor = "doctor-revoked";

        await Factory.ResetDatabaseAsync();

        var user = await Factory.CreateUserAsync(email, password);
        Guid hospitalId = (await Factory.EnsureHospitalAsync(PrimaryCode, "Primary"))
            .Id;
        await Factory.SeedMembershipAsync(user, hospitalId, actor);
        await Factory.RevokeMembershipAsync(user.Id, hospitalId);
        await Factory.RegisterClientAsync();
        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(email, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Hospital-Code", PrimaryCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetWorkspaceAsync(string accessToken)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Hospital-Code", PrimaryCode);

        return await client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ReadHospitalCodesAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        return [.. document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString() ?? string.Empty)];
    }

    private async Task<string> ResolveActorAsync(string accessToken, string hospitalCode)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Hospital-Code", hospitalCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        Assert.True(
            document.RootElement.TryGetProperty("actorId", out JsonElement actorElement));
        return actorElement.GetString() ?? string.Empty;
    }
}
