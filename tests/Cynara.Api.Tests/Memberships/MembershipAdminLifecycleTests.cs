using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Infrastructure.Modules.Identity;

namespace Cynara.Api.Tests.Memberships;

/// <summary>
/// Slice-2 admin add/update surface over the real-auth HTTP API: add
/// admits one Active row per (user, hospital) with 201 plus an atomic
/// audit event; cardinality and actor-taken conflicts surface 409 (never
/// 400); cross-hospital actor reuse succeeds; malformed actor ids fail
/// 400; unknown users fail 404; concurrent duplicate adds resolve
/// 201/409; update revokes the current row and inserts a new Active row
/// atomically; revoked targets reject with 409; capability-less callers
/// see 403 everywhere; listing exposes Active and Revoked history
/// newest-first.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed partial class MembershipAdminLifecycleTests : IDisposable
{
    private const string HospitalCode = "memb-admin";

    private const string OtherHospitalCode = "memb-other";

    private const string Password = "Cynara!Dev123";

    private const string ActorAdmin = "actor-admin";

    public MembershipAdminLifecycleTests(
        PostgreSqlDatabaseFixture database)
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
    public async Task Add_HappyPath_Returns201AndAuditsAtomically()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser member = await Factory.CreateUserAsync(
            "member@cynara.dev",
            Password).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new { userId = member.Id, actorId = "doctor-alpha" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement view = await ReadViewAsync(response)
            .ConfigureAwait(false);
        Assert.Equal(member.Id, view.GetProperty("userId").GetGuid());
        Assert.Equal(
            "doctor-alpha",
            view.GetProperty("actorId").GetString());
        Assert.Equal("Active", view.GetProperty("status").GetString());
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith(
            $"/api/memberships/{view.GetProperty("id").GetGuid()}",
            response.Headers.Location.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(1, await CountAuditsAsync("membership.added")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Add_SecondActiveForSameUser_Returns409AndWritesNothing()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser member = await Factory.CreateUserAsync(
            "cardinality@cynara.dev",
            Password).ConfigureAwait(false);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        await AddAsync(client, member.Id, "doctor-one")
            .ConfigureAwait(false);
        int rowsBefore = await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new { userId = member.Id, actorId = "doctor-two" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(rowsBefore, await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("membership.added")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Add_ActorTakenInHospital_Returns409Never400()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser first = await Factory.CreateUserAsync(
            "taken-1@cynara.dev",
            Password).ConfigureAwait(false);
        CynaraUser second = await Factory.CreateUserAsync(
            "taken-2@cynara.dev",
            Password).ConfigureAwait(false);
        await AddAsync(client, first.Id, "doctor-taken")
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new { userId = second.Id, actorId = "doctor-taken" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await CountAuditsAsync("membership.added")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Add_ActorReusedAcrossHospitals_Returns201()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser memberA = await Factory.CreateUserAsync(
            "reuse-a@cynara.dev",
            Password).ConfigureAwait(false);
        await AddAsync(client, memberA.Id, "doctor-shared")
            .ConfigureAwait(false);
        HttpClient other = await SeedOtherHospitalAsync()
            .ConfigureAwait(false);
        CynaraUser memberB = await Factory.CreateUserAsync(
            "reuse-b@cynara.dev",
            Password).ConfigureAwait(false);

        using HttpResponseMessage response = await other
            .PostAsJsonAsync(
                "/api/memberships",
                new { userId = memberB.Id, actorId = "doctor-shared" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await CountAuditsAsync("membership.added")
            .ConfigureAwait(false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("bad!char")]
    [InlineData("-leading-hyphen")]
    [InlineData("_leading-underscore")]
    public async Task Add_MalformedActorId_Returns400AndWritesNothing(
        string actorId)
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser member = await Factory.CreateUserAsync(
            $"malformed-{Guid.NewGuid():N}@cynara.dev",
            Password).ConfigureAwait(false);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        int rowsBefore = await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new { userId = member.Id, actorId })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(rowsBefore, await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountAuditsAsync("membership.added")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Add_ActorIdOver128Characters_Returns400()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser member = await Factory.CreateUserAsync(
            "long-actor@cynara.dev",
            Password).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new { userId = member.Id, actorId = new string('a', 129) })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_UnknownUser_Returns404AndWritesNothing()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        int rowsBefore = await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new
                {
                    userId = Guid.NewGuid(),
                    actorId = "doctor-ghost",
                })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(rowsBefore, await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Add_ConcurrentDuplicates_Resolve201And409()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser member = await Factory.CreateUserAsync(
            "race@cynara.dev",
            Password).ConfigureAwait(false);
        object payload = new
        {
            userId = member.Id,
            actorId = "doctor-race",
        };

        Task<HttpResponseMessage> CallAsync()
        {
            return client.PostAsJsonAsync("/api/memberships", payload);
        }

        Task<HttpResponseMessage> firstCall = CallAsync();
        Task<HttpResponseMessage> secondCall = CallAsync();
        using HttpResponseMessage first = await firstCall
            .ConfigureAwait(false);
        using HttpResponseMessage second = await secondCall
            .ConfigureAwait(false);

        var statuses = new[]
        {
            first.StatusCode,
            second.StatusCode,
        };
        Array.Sort(statuses);
        Assert.Equal(
            [
                HttpStatusCode.Created,
                HttpStatusCode.Conflict,
            ],
            statuses);
        Assert.Equal(1, await CountActiveForUserAsync(member.Id)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Update_HappyPath_RevokesCurrentAndInsertsNew()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser member = await Factory.CreateUserAsync(
            "update@cynara.dev",
            Password).ConfigureAwait(false);
        Guid currentId = await AddAsync(client, member.Id, "doctor-old")
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                $"/api/memberships/{currentId}/update",
                new { actorId = "doctor-new" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement view = await ReadViewAsync(response)
            .ConfigureAwait(false);
        Guid nextId = view.GetProperty("id").GetGuid();
        Assert.NotEqual(currentId, nextId);
        Assert.Equal(
            "doctor-new",
            view.GetProperty("actorId").GetString());
        Assert.Equal("Active", view.GetProperty("status").GetString());
        MembershipRow current = await LoadMembershipAsync(currentId)
            .ConfigureAwait(false);
        Assert.Equal("Revoked", current.Status);
        Assert.NotNull(current.RevokedAt);
        Assert.Equal(1, await CountAuditsAsync("membership.added")
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("membership.updated")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Update_RevokedTarget_Returns409AndChangesNothing()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser member = await Factory.CreateUserAsync(
            "update-revoked@cynara.dev",
            Password).ConfigureAwait(false);
        Guid currentId = await AddAsync(client, member.Id, "doctor-stale")
            .ConfigureAwait(false);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        await Factory.RevokeMembershipAsync(member.Id, hospitalId)
            .ConfigureAwait(false);
        int rowsBefore = await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                $"/api/memberships/{currentId}/update",
                new { actorId = "doctor-fresh" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(rowsBefore, await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountAuditsAsync("membership.updated")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Routes_RejectActorsWithoutCapabilityGrant()
    {
        HttpClient client = await SeedUnprivilegedAsync()
            .ConfigureAwait(false);

        using HttpResponseMessage list = await client
            .GetAsync("/api/memberships").ConfigureAwait(false);
        using HttpResponseMessage create = await client
            .PostAsJsonAsync(
                "/api/memberships",
                new
                {
                    userId = Guid.NewGuid(),
                    actorId = "doctor-denied",
                })
            .ConfigureAwait(false);
        using HttpResponseMessage update = await client
            .PostAsJsonAsync(
                $"/api/memberships/{Guid.NewGuid()}/update",
                new { actorId = "doctor-denied" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task List_IncludesHistoryNewestFirstWithStatus()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        CynaraUser active = await Factory.CreateUserAsync(
            "listed-active@cynara.dev",
            Password).ConfigureAwait(false);
        CynaraUser historic = await Factory.CreateUserAsync(
            "listed-history@cynara.dev",
            Password).ConfigureAwait(false);
        _ = await AddAsync(client, active.Id, "doctor-listed")
            .ConfigureAwait(false);
        _ = await AddAsync(client, historic.Id, "doctor-historic")
            .ConfigureAwait(false);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        await Factory.RevokeMembershipAsync(historic.Id, hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync("/api/memberships").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response)
            .ConfigureAwait(false);
        List<JsonElement> items =
            [.. document.RootElement.EnumerateArray()];
        Assert.Equal(3, items.Count);
        Assert.Equal(
            "Revoked",
            items[0].GetProperty("status").GetString());
        Assert.Equal(
            historic.Id,
            items[0].GetProperty("userId").GetGuid());
        DateTimeOffset first = items[0].GetProperty("createdAt")
            .GetDateTimeOffset();
        DateTimeOffset second = items[1].GetProperty("createdAt")
            .GetDateTimeOffset();
        Assert.True(first >= second);
        Assert.Contains(
            items,
            item => string.Equals(
                item.GetProperty("status").GetString(),
                "Active",
                StringComparison.Ordinal));
    }
}
