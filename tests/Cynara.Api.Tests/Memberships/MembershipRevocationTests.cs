using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Identity;

namespace Cynara.Api.Tests.Memberships;

/// <summary>
/// Slice-3 revoke/reactivate surface over the real-auth HTTP API: revoke
/// flips an Active row to Revoked with an atomic audit event and the actor
/// immediately loses resolution; a second revoke conflicts; cross-hospital
/// ids stay invisible; reactivate inserts a NEW Active row while the
/// revoked row is retained; reactivation with an Active row present
/// conflicts; revoked actor ids become reusable.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed partial class MembershipRevocationTests : IDisposable
{
    private const string HospitalCode = "memb-revoke";

    private const string OtherHospitalCode = "memb-revoke-other";

    private const string Password = "Cynara!Dev123";

    private const string ActorAdmin = "actor-admin";

    public MembershipRevocationTests(PostgreSqlDatabaseFixture database)
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
    public async Task Revoke_HappyPath_Returns200AndRetainsRevokedRow()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        IdentityUser<Guid> member = await Factory.CreateUserAsync(
            "revoked-happy@cynara.dev",
            Password).ConfigureAwait(false);
        Guid currentId = await AddAsync(client, member.Id, "doctor-gone")
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsync(
                $"/api/memberships/{currentId}/revoke",
                content: null)
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement view = await ReadViewAsync(response)
            .ConfigureAwait(false);
        Assert.Equal("Revoked", view.GetProperty("status").GetString());
        Assert.NotNull(
            view.GetProperty("revokedAt").GetString());
        MembershipRow current = await LoadMembershipAsync(currentId)
            .ConfigureAwait(false);
        Assert.Equal("Revoked", current.Status);
        Assert.NotNull(current.RevokedAt);
        Assert.Equal(1, await CountAuditsAsync("membership.revoked")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Revoke_RevokedActor_LosesResolution()
    {
        const string email = "revoked-loses@cynara.dev";
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        IdentityUser<Guid> member = await Factory.CreateUserAsync(
            email,
            Password).ConfigureAwait(false);
        Guid currentId = await AddAsync(client, member.Id, "doctor-lost")
            .ConfigureAwait(false);
        using HttpResponseMessage revoke = await client
            .PostAsync(
                $"/api/memberships/{currentId}/revoke",
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        AuthTokenResult tokens = await Factory
            .GetPasswordTokenAsync(email, Password).ConfigureAwait(false);
        HttpClient memberClient = Factory.CreateClient();
        memberClient.DefaultRequestHeaders.Authorization =
            new("Bearer", tokens.AccessToken);
        memberClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            HospitalCode);
        using HttpResponseMessage capabilities = await memberClient
            .GetAsync("/api/me/capabilities").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, capabilities.StatusCode);
    }

    [Fact]
    public async Task Revoke_Twice_SecondReturns409AndChangesNothing()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        IdentityUser<Guid> member = await Factory.CreateUserAsync(
            "revoked-twice@cynara.dev",
            Password).ConfigureAwait(false);
        Guid currentId = await AddAsync(client, member.Id, "doctor-twice")
            .ConfigureAwait(false);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage first = await client
            .PostAsync(
                $"/api/memberships/{currentId}/revoke",
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        int rowsBefore = await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage second = await client
            .PostAsync(
                $"/api/memberships/{currentId}/revoke",
                content: null)
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(rowsBefore, await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("membership.revoked")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Revoke_CrossHospital_Returns404AndChangesNothing()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        HttpClient other = await SeedOtherHospitalAsync()
            .ConfigureAwait(false);
        IdentityUser<Guid> member = await Factory.CreateUserAsync(
            "revoked-foreign@cynara.dev",
            Password).ConfigureAwait(false);
        Guid currentId = await AddAsync(other, member.Id, "doctor-away")
            .ConfigureAwait(false);
        Guid otherHospitalId = await HospitalIdAsync(OtherHospitalCode)
            .ConfigureAwait(false);
        int rowsBefore = await CountMembershipsAsync(otherHospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsync(
                $"/api/memberships/{currentId}/revoke",
                content: null)
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(rowsBefore, await CountMembershipsAsync(otherHospitalId)
            .ConfigureAwait(false));
        Assert.Equal("Active", (await LoadMembershipAsync(currentId)
            .ConfigureAwait(false)).Status);
    }

    [Fact]
    public async Task Reactivate_HappyPath_InsertsNewActiveRow()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        IdentityUser<Guid> member = await Factory.CreateUserAsync(
            "reactivated@cynara.dev",
            Password).ConfigureAwait(false);
        Guid revokedId = await AddAsync(client, member.Id, "doctor-back")
            .ConfigureAwait(false);
        using HttpResponseMessage revoke = await client
            .PostAsync(
                $"/api/memberships/{revokedId}/revoke",
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        int rowsBefore = await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                $"/api/memberships/{revokedId}/reactivate",
                new { actorId = "doctor-back" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement view = await ReadViewAsync(response)
            .ConfigureAwait(false);
        Guid nextId = view.GetProperty("id").GetGuid();
        Assert.NotEqual(revokedId, nextId);
        Assert.Equal("Active", view.GetProperty("status").GetString());
        Assert.Equal(
            "doctor-back",
            view.GetProperty("actorId").GetString());
        Assert.Equal(rowsBefore + 1, await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false));
        Assert.Equal("Revoked", (await LoadMembershipAsync(revokedId)
            .ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("membership.activated")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Reactivate_WithActivePresent_Returns409()
    {
        HttpClient client = await SeedAdminAsync().ConfigureAwait(false);
        IdentityUser<Guid> member = await Factory.CreateUserAsync(
            "reactivate-busy@cynara.dev",
            Password).ConfigureAwait(false);
        Guid revokedId = await AddAsync(client, member.Id, "doctor-old")
            .ConfigureAwait(false);
        using HttpResponseMessage revoke = await client
            .PostAsync(
                $"/api/memberships/{revokedId}/revoke",
                content: null)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        using HttpResponseMessage revived = await client
            .PostAsJsonAsync(
                $"/api/memberships/{revokedId}/reactivate",
                new { actorId = "doctor-busy-new" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, revived.StatusCode);
        Guid hospitalId = await HospitalIdAsync(HospitalCode)
            .ConfigureAwait(false);
        int rowsBefore = await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                $"/api/memberships/{revokedId}/reactivate",
                new { actorId = "doctor-busy-latest" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(rowsBefore, await CountMembershipsAsync(hospitalId)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("membership.activated")
            .ConfigureAwait(false));
    }
}
