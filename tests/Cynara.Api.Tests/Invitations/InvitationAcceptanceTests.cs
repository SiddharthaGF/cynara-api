using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Cynara.Domain.Capabilities;
using Cynara.Domain.Invitations;

using Microsoft.AspNetCore.Identity;

namespace Cynara.Api.Tests.Invitations;

/// <summary>
/// Anonymous invitation acceptance over the public surface: a valid link
/// establishes credentials, membership, and capability grants atomically;
/// every token-state failure returns one byte-identical uniform envelope;
/// weak passwords, taken actor ids, and existing memberships return 400;
/// every transition is audited in the same commit.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed partial class InvitationAcceptanceTests : IDisposable
{
    private const string HospitalCode = "inv-accept";
    private const string HospitalName = "Hospital A";
    private const string AdminEmail = "admin@cynara.dev";
    private const string ActorAdmin = "actor-admin";
    private const string ActorInvitee = "actor-invitee";
    private const string Password = "Cynara!Dev123";
    private const string InviteeEmail = "invitee@cynara.dev";
    private const string WeakPassword = "weak";

    private const string ConformingSnapshot =
        /*lang=json,strict*/
        """{"actorId":"actor-invitee","capabilities":["patients.read","audit.read"]}""";

    private const string UnknownCodeSnapshot =
        /*lang=json,strict*/
        """{"actorId":"actor-invitee","capabilities":["not-a-real-code"]}""";

    private const string NonConformingSnapshot =
        /*lang=json,strict*/
        """{"actorId":"actor-invitee","capabilities":[],"extra":true}""";

    /// <summary>
    /// Deliberately invalid JSON: the unquoted key is built at runtime so no
    /// source literal carries a JSON shape.
    /// </summary>
    private static string MalformedSnapshot()
    {
        return string.Concat("{", "actorId", "}");
    }

    public InvitationAcceptanceTests(PostgreSqlDatabaseFixture database)
    {
        Database = database.Settings;
        Factory = new RecordingInvitationFactory(Database);
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private RecordingInvitationFactory Factory { get; }

    private TestDatabaseSettings Database { get; }

    [Fact]
    public async Task Accept_WithValidTokenAndPassword_EstablishesUserMembershipAndGrants()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(raw);
        JsonElement root = body.RootElement;
        Assert.True(root.GetProperty("accepted").GetBoolean());
        JsonElement member = root.GetProperty("member");
        Assert.Equal(
            InviteeEmail,
            member.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal(
            ActorInvitee,
            member.GetProperty("actor").GetProperty("id").GetString());
        Assert.Equal(
            HospitalCode,
            member.GetProperty("hospital").GetProperty("code").GetString());
        Assert.Equal(
            HospitalName,
            member.GetProperty("hospital").GetProperty("name").GetString());
        string[] capabilities = [.. member.GetProperty("capabilities")
            .EnumerateArray()
            .Select(static item => item.GetString()!)];
        Assert.Equal(["patients.read", "audit.read"], capabilities);

        Invitation row = await LoadInvitationAsync(id).ConfigureAwait(false);
        Assert.Equal(InvitationStatus.Accepted, row.Status);
        Assert.NotNull(row.UserId);
        Assert.NotNull(row.AcceptedAt);
        Assert.Equal(1, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.True((await LoadUserAsync(InviteeEmail).ConfigureAwait(false))
            .EmailConfirmed);
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(2, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        IReadOnlyList<CapabilityAssignment> grants = await LoadGrantsAsync(
            hospitalId, ActorInvitee).ConfigureAwait(false);
        Assert.Equal(2, grants.Count);
        Assert.All(grants, grant => Assert.Null(grant.AssignedBy));
        Assert.Equal(1, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WithExistingUser_CreatesMembershipAndGrantsOnly()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        _ = await Factory.CreateUserAsync(InviteeEmail, Password)
            .ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(2, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(
            InvitationStatus.Accepted,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WithWeakPassword_Returns400AndChangesNothing()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, WeakPassword).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(0, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WithActorIdAlreadyInHospital_Returns400AndChangesNothing()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        IdentityUser<Guid> other = await Factory.CreateUserAsync(
            "other@cynara.dev", Password).ConfigureAwait(false);
        await Factory.SeedMembershipAsync(other, hospitalId, ActorInvitee)
            .ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(0, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_ExistingUserWithMembershipInHospital_Returns400()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        IdentityUser<Guid> existing = await Factory.CreateUserAsync(
            InviteeEmail, Password).ConfigureAwait(false);
        await Factory.SeedMembershipAsync(existing, hospitalId, "actor-other")
            .ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, "actor-other")
            .ConfigureAwait(false));
        Assert.Equal(0, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_ReinviteAfterRevoke_CreatesNewActiveMembership()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        IdentityUser<Guid> existing = await Factory.CreateUserAsync(
            InviteeEmail, Password).ConfigureAwait(false);
        await Factory.SeedMembershipAsync(existing, hospitalId, "actor-old")
            .ConfigureAwait(false);
        await Factory.RevokeMembershipAsync(existing.Id, hospitalId)
            .ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            InvitationStatus.Accepted,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, "actor-old")
            .ConfigureAwait(false));
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(2, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_RevokedActorId_IsReusable()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        IdentityUser<Guid> other = await Factory.CreateUserAsync(
            "other@cynara.dev", Password).ConfigureAwait(false);
        await Factory.SeedMembershipAsync(other, hospitalId, ActorInvitee)
            .ConfigureAwait(false);
        await Factory.RevokeMembershipAsync(other.Id, hospitalId)
            .ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            InvitationStatus.Accepted,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(2, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WhenIdentityUserCreationFails_RollsBackEverything()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        (Guid id, string token) = await SeedInvitationAsync(
            hospitalId,
            email: " ",
            status: InvitationStatus.Pending,
            snapshot: ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(0, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_TokenStateFailures_ReturnByteIdenticalEnvelope()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        (_, string cancelledToken) = await SeedInvitationAsync(
            hospitalId,
            email: "cancelled@cynara.dev",
            status: InvitationStatus.Cancelled,
            snapshot: ConformingSnapshot).ConfigureAwait(false);
        (_, string revokedToken) = await SeedInvitationAsync(
            hospitalId,
            email: "revoked@cynara.dev",
            status: InvitationStatus.Revoked,
            snapshot: ConformingSnapshot).ConfigureAwait(false);
        (_, string usedToken) = await SeedInvitationAsync(
            hospitalId,
            email: "used@cynara.dev",
            status: InvitationStatus.AlreadyUsed,
            snapshot: ConformingSnapshot).ConfigureAwait(false);
        (_, string expiredToken) = await SeedInvitationAsync(
            hospitalId,
            email: "expired@cynara.dev",
            status: InvitationStatus.Expired,
            snapshot: ConformingSnapshot).ConfigureAwait(false);
        string unknownToken = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));
        using HttpClient anonymous = Factory.CreateClient();

        List<string> bodies = [];
        foreach (string token in new[]
            {
                unknownToken,
                cancelledToken,
                revokedToken,
                usedToken,
                expiredToken,
            })
        {
            using HttpResponseMessage response = await AcceptAsync(
                anonymous, token, Password).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false));
        }

        Assert.Equal(5, bodies.Count);
        Assert.All(
            bodies,
            body => Assert.Equal(/*lang=json,strict*/ """{"accepted":false}""", body));
    }

    [Fact]
    public async Task Accept_ExpiredLink_LazilyTransitionsToExpiredAndAudits()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        (Guid id, string token) = await SeedInvitationAsync(
            hospitalId,
            email: InviteeEmail,
            status: InvitationStatus.Pending,
            snapshot: ConformingSnapshot,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1))
            .ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Equal(/*lang=json,strict*/ """{"accepted":false}""", body);
        Assert.Equal(
            InvitationStatus.Expired,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("invitation.expired")
            .ConfigureAwait(false));
        Assert.Equal(0, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_AcceptedLinkSecondPresentation_TransitionsToAlreadyUsed()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage first = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        string firstBody = await first.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            "\"accepted\":true",
            firstBody,
            StringComparison.Ordinal);

        using HttpResponseMessage second = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        string secondBody = await second.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Equal(/*lang=json,strict*/ """{"accepted":false}""", secondBody);

        Assert.Equal(
            InvitationStatus.AlreadyUsed,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
        Assert.Equal(1, await CountAuditsAsync("invitation.already-used")
            .ConfigureAwait(false));
        Assert.Equal(1, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(2, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WithMalformedSnapshot_ReturnsUniformAndAuditsFailure()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        (Guid id, string token) = await SeedInvitationAsync(
            hospitalId,
            email: InviteeEmail,
            status: InvitationStatus.Pending,
            snapshot: MalformedSnapshot()).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Equal(/*lang=json,strict*/ """{"accepted":false}""", body);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("invitation.acceptance-failed")
            .ConfigureAwait(false));
        Assert.Equal(0, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WithNonConformingSnapshot_ReturnsUniformAndAuditsFailure()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        (Guid id, string token) = await SeedInvitationAsync(
            hospitalId,
            email: InviteeEmail,
            status: InvitationStatus.Pending,
            snapshot: NonConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Equal(/*lang=json,strict*/ """{"accepted":false}""", body);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("invitation.acceptance-failed")
            .ConfigureAwait(false));
        Assert.Equal(0, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WithUnknownCapabilityCode_ReturnsUniformAndAuditsFailure()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, UnknownCodeSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Equal(/*lang=json,strict*/ """{"accepted":false}""", body);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("invitation.acceptance-failed")
            .ConfigureAwait(false));
        Assert.Equal(0, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_WithMissingSnapshot_ReturnsUniformAndAuditsFailure()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        using HttpClient admin = await SeedAdminClientAsync(hospitalId)
            .ConfigureAwait(false);
        (Guid id, string token) = await CreateInvitationAsync(
            admin, InviteeEmail, snapshot: null).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Equal(/*lang=json,strict*/ """{"accepted":false}""", body);
        Assert.Equal(
            InvitationStatus.Pending,
            (await LoadInvitationAsync(id).ConfigureAwait(false)).Status);
        Assert.Equal(1, await CountAuditsAsync("invitation.acceptance-failed")
            .ConfigureAwait(false));
        Assert.Equal(0, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(0, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
    }

    [Fact]
    public async Task Accept_AfterTenPostingsFromSameIp_Returns429WithRetryAfter()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        (_, string token) = await SeedInvitationAsync(
            hospitalId,
            email: InviteeEmail,
            status: InvitationStatus.Pending,
            snapshot: ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            using HttpResponseMessage response = await AcceptAsync(
                anonymous, token, Password).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using HttpResponseMessage rejected = await AcceptAsync(
            anonymous, token, Password).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out _));
    }

    [Fact]
    public async Task Accept_SameTokenConcurrently_OneWinsOneGetsUniformEnvelope()
    {
        Guid hospitalId = await SeedWorkspaceAsync().ConfigureAwait(false);
        (Guid id, string token) = await SeedInvitationAsync(
            hospitalId,
            email: InviteeEmail,
            status: InvitationStatus.Pending,
            snapshot: ConformingSnapshot).ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();

        Task<HttpResponseMessage> first = AcceptAsync(
            anonymous, token, Password);
        Task<HttpResponseMessage> second = AcceptAsync(
            anonymous, token, Password);
        using HttpResponseMessage firstResponse = await first
            .ConfigureAwait(false);
        using HttpResponseMessage secondResponse = await second
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        string firstBody = await firstResponse.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        string secondBody = await secondResponse.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        string[] bodies = [firstBody, secondBody];
        Assert.Equal(1, bodies.Count(body => body.Contains(
            "\"accepted\":true", StringComparison.Ordinal)));
        Assert.Equal(1, bodies.Count(body => body.Equals(
            /*lang=json,strict*/ """{"accepted":false}""",
            StringComparison.Ordinal)));

        Assert.Equal(1, await CountUsersAsync(InviteeEmail)
            .ConfigureAwait(false));
        Assert.Equal(1, await CountMembershipsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));
        Assert.Equal(2, await CountGrantsAsync(hospitalId, ActorInvitee)
            .ConfigureAwait(false));

        Invitation row = await LoadInvitationAsync(id).ConfigureAwait(false);
        Assert.True(
            row.Status is InvitationStatus.Accepted
                or InvitationStatus.AlreadyUsed);
        Assert.Equal(1, await CountAuditsAsync("invitation.accepted")
            .ConfigureAwait(false));
        Assert.True(await CountAuditsAsync("invitation.already-used")
                .ConfigureAwait(false) <= 1);
    }
}
