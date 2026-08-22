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
/// Privacy payload and read-audit contract of the user directory: list and
/// detail payloads expose exactly their documented key sets (never roles),
/// a foreign-hospital identifier returns a 404 indistinguishable from an
/// unknown identifier, successful details emit exactly one sensitive-read
/// audit event, and listings emit none.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class UserDirectoryPayloadAuditTests : IDisposable
{
    private const string HospitalACode = "payload-a";
    private const string HospitalBCode = "payload-b";
    private const string Password = "Cynara!Dev123";

    private static readonly string[] TargetActorIds = ["actor-view"];

    private static readonly string[] TargetCapabilityCodes =
        ["patients.read", "workspace.read"];

    public UserDirectoryPayloadAuditTests(PostgreSqlDatabaseFixture database)
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
    public async Task ListItem_ExposesExactlyIdEmailHospitals_NeverRoles()
    {
        await Factory.ResetDatabaseAsync();
        await SeedCallerAndViewAsync();

        HttpClient client = await CreateAdminClientAsync();
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/users", UriKind.Relative)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        foreach (JsonElement item in document.RootElement
            .GetProperty("items").EnumerateArray())
        {
            AssertKeySet(item, "id", "email", "hospitals");
            Assert.Equal(
                JsonValueKind.Array,
                item.GetProperty("hospitals").ValueKind);
            Assert.False(item.TryGetProperty("roles", out _));
        }
    }

    [Fact]
    public async Task Detail_EnrichesWithinPolicy_WithExactKeySets()
    {
        (IdentityUser<Guid> target, _) = await SeedCallerAndViewAsync();
        HttpClient client = await CreateAdminClientAsync();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/api/users/{target.Id}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        JsonElement detail = document.RootElement;
        AssertKeySet(
            detail,
            "id",
            "email",
            "userName",
            "memberships",
            "capabilities",
            "flags");
        Assert.False(detail.TryGetProperty("roles", out _));
        Assert.Equal(
            HospitalACode,
            detail.GetProperty("memberships")[0]
                .GetProperty("hospital").GetString());
        Assert.Equal(
            TargetActorIds,
            detail.GetProperty("memberships").EnumerateArray()
                .Select(membership => membership.GetProperty("actorId")
                    .GetString()));
        AssertKeySet(
            detail.GetProperty("memberships")[0],
            "hospital",
            "actorId",
            "createdAt");
        Assert.Equal(
            TargetCapabilityCodes,
            detail.GetProperty("capabilities").EnumerateArray()
                .Select(capability => capability.GetString()));
        JsonElement flags = detail.GetProperty("flags");

        // The spec pins the flag key set, not seeded account values: lockout
        // configuration is environment state owned by the auth feature.
        AssertKeySet(flags, "emailConfirmed", "lockoutEnabled", "lockoutEnd");
    }

    [Fact]
    public async Task ForeignHospitalId_Returns404_IdenticalToUnknown()
    {
        (IdentityUser<Guid> _, IdentityUser<Guid> foreign) =
            await SeedCallerAndViewAsync();
        var unknownId = Guid.NewGuid();
        HttpClient client = await CreateAdminClientAsync();

        using HttpResponseMessage foreignResponse = await client.GetAsync(
            new Uri($"/api/users/{foreign.Id}", UriKind.Relative))
            .ConfigureAwait(false);
        using HttpResponseMessage unknownResponse = await client.GetAsync(
            new Uri($"/api/users/{unknownId}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        string foreignBody = NormalizeIds(
            await foreignResponse.Content.ReadAsStringAsync()
                .ConfigureAwait(false),
            foreign.Id);
        string unknownBody = NormalizeIds(
            await unknownResponse.Content.ReadAsStringAsync()
                .ConfigureAwait(false),
            unknownId);
        Assert.Equal(foreignBody, unknownBody);
    }

    [Fact]
    public async Task Details_EmitOneReadAudit_Lists_EmitNone()
    {
        (IdentityUser<Guid> target, _) = await SeedCallerAndViewAsync();
        HttpClient client = await CreateAdminClientAsync();

        using HttpResponseMessage list = await client.GetAsync(
            new Uri("/api/users", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(0, await CountReadAuditsAsync(target.Id));

        using HttpResponseMessage firstDetail = await client.GetAsync(
            new Uri($"/api/users/{target.Id}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, firstDetail.StatusCode);
        int afterFirst = await CountReadAuditsAsync(target.Id);

        using HttpResponseMessage secondDetail = await client.GetAsync(
            new Uri($"/api/users/{target.Id}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secondDetail.StatusCode);

        Assert.Equal(1, afterFirst);
        Assert.Equal(2, await CountReadAuditsAsync(target.Id));
    }

    /// <summary>
    /// Seeds the shared scenario: hospital A holds the admin caller plus a
    /// seeded view target whose actor carries two capability grants; hospital
    /// B holds a foreign user invisible to hospital-scoped callers.
    /// </summary>
    private async Task<(
        IdentityUser<Guid> Target,
        IdentityUser<Guid> Foreign)> SeedCallerAndViewAsync()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.RegisterClientAsync();
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        Guid hospitalB = (await Factory.EnsureHospitalAsync(
            HospitalBCode,
            "Hospital B")).Id;

        IdentityUser<Guid> target = await Factory.CreateUserAsync(
            "view-target@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(target, hospitalA, "actor-view");
        await Factory.SeedCapabilityAsync(
            hospitalA,
            "actor-view",
            CapabilityCodes.PatientsRead);
        await Factory.SeedCapabilityAsync(
            hospitalA,
            "actor-view",
            CapabilityCodes.WorkspaceRead);

        IdentityUser<Guid> foreign = await Factory.CreateUserAsync(
            "foreign@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(foreign, hospitalB, "actor-foreign");

        return (target, foreign);
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        Guid hospitalA = (await Factory.EnsureHospitalAsync(
            HospitalACode,
            "Hospital A")).Id;
        IdentityUser<Guid> admin = await Factory.CreateUserAsync(
            "admin@cynara.dev",
            Password);
        await Factory.SeedMembershipAsync(admin, hospitalA, "actor-admin");
        await Factory.SeedCapabilityAsync(
            hospitalA,
            "actor-admin",
            CapabilityCodes.UsersRead);

        AuthTokenResult tokens = await Factory.GetPasswordTokenAsync(
            "admin@cynara.dev",
            Password);
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            HospitalACode);
        return client;
    }

    private async Task<int> CountReadAuditsAsync(Guid targetId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .Where(item => item.Action == "user.read"
                && item.ResourceType == "user"
                && item.ResourceId == targetId)
            .CountAsync()
            .ConfigureAwait(false);
    }

    private static void AssertKeySet(
        JsonElement element,
        params string[] expectedKeys)
    {
        Assert.Equal(
            expectedKeys.Order(StringComparer.Ordinal),
            element.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    private static string NormalizeIds(string body, Guid id)
    {
        return body.Replace(id.ToString(), "<id>", StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }
}
