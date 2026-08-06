using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Capabilities;

/// <summary>
/// End-to-end capability enforcement against the real Postgres-backed host.
/// The factory is built with <c>grantAllCapabilities: false</c> so the real
/// <see cref="Application.Modules.Capabilities.EffectiveCapabilityResolver"/>
/// and repository drive both the endpoint-level authorization filter and the
/// domain-boundary guards. Coverage: allowed / denied / write-denied /
/// cross-tenant / revocation / <c>api/me/capabilities</c> /
/// HTTP grant-and-revoke workflow.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class CapabilityEnforcementTests : IAsyncDisposable
{
    private const string PrimaryHospitalCode =
        CynaraTenantWebApplicationFactory.PrimaryCode;

    private const string OtherHospitalCode =
        CynaraTenantWebApplicationFactory.OtherCode;

    private const string Doctor = "doctor";
    private const string Nurse = "nurse";
    private const string Admin = "admin";

    public CapabilityEnforcementTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(
            database.Settings,
            grantAllCapabilities: false);
        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetPatients_Returns200_WhenActorHoldsReadCapability()
    {
        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PatientsRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPatients_Returns403_WhenActorHoldsNoGrant()
    {
        HttpClient client = CreateClient(Nurse, PrimaryHospitalCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        int deniedCount = await CountAuditEventsAsync("access.denied")
            .ConfigureAwait(false);
        Assert.True(deniedCount >= 1, "Expected access.denied audit event.");
    }

    [Fact]
    public async Task GetPatients_Returns403_WhenNoActorIdentity()
    {
        HttpClient client = CreateClient(actorId: null, PrimaryHospitalCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task CreatePatient_Returns403_WhenOnlyReadCapabilityGranted()
    {
        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PatientsRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await PostVndAsync(
            client,
            new Uri("/api/patients", UriKind.Relative),
            new
            {
                mrn = "MRN-FORBIDDEN",
                givenName = "Ada",
                familyName = "Lovelace",
                birthDate = "2000-01-01",
                sex = "female",
            }).ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task AssignmentInOneHospital_DoesNotAuthorizeAnother()
    {
        HttpClient primaryClient = CreateClient(Doctor, PrimaryHospitalCode);
        HttpClient otherClient = CreateClient(Doctor, OtherHospitalCode);
        await Factory.SeedSecondaryHospitalAsync().ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PatientsRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage denied = await otherClient
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);
        await AssertForbiddenAsync(denied).ConfigureAwait(false);

        using HttpResponseMessage allowed = await primaryClient
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task RevokingCapability_DeniesSubsequentRequests()
    {
        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PatientsRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using (HttpResponseMessage allowed = await client
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        await RemoveAssignmentAsync(
            Doctor,
            CapabilityCodes.PatientsRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage denied = await client
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);
        await AssertForbiddenAsync(denied).ConfigureAwait(false);
    }

    [Fact]
    public async Task MeCapabilities_ReflectsEffectiveSet()
    {
        HttpClient doctorClient = CreateClient(Doctor, PrimaryHospitalCode);
        HttpClient nurseClient = CreateClient(Nurse, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PatientsRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PatientsWrite,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage doctorResponse = await doctorClient
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, doctorResponse.StatusCode);
        using JsonDocument doctorBody = await JsonDocument.ParseAsync(
            await doctorResponse.Content.ReadAsStreamAsync()
                .ConfigureAwait(false)).ConfigureAwait(false);
        Assert.Equal(
            Doctor,
            doctorBody.RootElement.GetProperty("actorId").GetString());
        string[] doctorCapabilities =
        [
            .. doctorBody.RootElement.GetProperty("capabilities")
                .EnumerateArray()
                .Select(static item => item.GetString() ?? string.Empty),
        ];
        Assert.Equal(
            [CapabilityCodes.PatientsRead, CapabilityCodes.PatientsWrite],
            doctorCapabilities);

        using HttpResponseMessage nurseResponse = await nurseClient
            .GetAsync(new Uri("/api/me/capabilities", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, nurseResponse.StatusCode);
        using JsonDocument nurseBody = await JsonDocument.ParseAsync(
            await nurseResponse.Content.ReadAsStreamAsync()
                .ConfigureAwait(false)).ConfigureAwait(false);
        Assert.Equal(Nurse, nurseBody.RootElement.GetProperty("actorId").GetString());
        Assert.Empty(nurseBody.RootElement.GetProperty("capabilities").EnumerateArray());
    }

    [Fact]
    public async Task GrantAndRevokeThroughHttp_AuthorizesAndDeniesProtectedEndpoint()
    {
        HttpClient adminClient = CreateClient(Admin, PrimaryHospitalCode);
        HttpClient doctorClient = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Admin,
            CapabilityCodes.CapabilitiesRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        await SeedAssignmentAsync(
            Admin,
            CapabilityCodes.CapabilitiesWrite,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage grantResponse = await PostVndAsync(
            adminClient,
            new Uri("/api/capabilities", UriKind.Relative),
            new
            {
                actorId = Doctor,
                capability = CapabilityCodes.PatientsRead,
            }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, grantResponse.StatusCode);

        using (HttpResponseMessage allowed = await doctorClient
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using HttpResponseMessage listResponse = await adminClient
            .GetAsync(new Uri("/api/capabilities", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using JsonDocument listBody = await JsonDocument.ParseAsync(
            await listResponse.Content.ReadAsStreamAsync()
                .ConfigureAwait(false)).ConfigureAwait(false);
        JsonElement items = listBody.RootElement.GetProperty("items");
        Assert.True(
            items.GetArrayLength() >= 2,
            "Expected admin grants plus the HTTP-created assignment.");

        using HttpResponseMessage revokeResponse = await adminClient
            .DeleteAsync(
                new Uri(
                    $"/api/capabilities/{Doctor}/{CapabilityCodes.PatientsRead}",
                    UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using HttpResponseMessage denied = await doctorClient
            .GetAsync(new Uri("/api/patients", UriKind.Relative))
            .ConfigureAwait(false);
        await AssertForbiddenAsync(denied).ConfigureAwait(false);
    }

    [Fact]
    public async Task CapabilityList_RequiresReadCapability()
    {
        HttpClient anonymousClient = CreateClient(Nurse, PrimaryHospitalCode);

        using HttpResponseMessage denied = await anonymousClient
            .GetAsync(new Uri("/api/capabilities", UriKind.Relative))
            .ConfigureAwait(false);
        await AssertForbiddenAsync(denied).ConfigureAwait(false);

        HttpClient adminClient = CreateClient(Admin, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Admin,
            CapabilityCodes.CapabilitiesRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage allowed = await adminClient
            .GetAsync(new Uri("/api/capabilities", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostVndAsync(
        HttpClient client,
        Uri requestUri,
        object payload)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/vnd.api+json");
        return await client
            .PostAsync(requestUri, content)
            .ConfigureAwait(false);
    }

    private HttpClient CreateClient(string? actorId, string hospitalCode)
    {
        HttpClient client = Factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            hospitalCode);
        if (actorId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Actor-Id",
                actorId);
        }

        return client;
    }

    private async Task SeedAssignmentAsync(
        string actorId,
        string capability,
        string hospitalCode)
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Factory.CreateScope();
        Hospital hospital = await scope
            .LoadHospitalAsync(hospitalCode)
            .ConfigureAwait(false);
        scope.DbContext.CapabilityAssignments.Add(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospital.Id,
            ActorId = actorId,
            Capability = capability,
            AssignedAt = DateTimeOffset.UtcNow,
        });
        _ = await scope.DbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task RemoveAssignmentAsync(
        string actorId,
        string capability,
        string hospitalCode)
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Factory.CreateScope();
        Hospital hospital = await scope
            .LoadHospitalAsync(hospitalCode)
            .ConfigureAwait(false);
        CapabilityAssignment assignment = await scope.DbContext
            .CapabilityAssignments
            .SingleAsync(item =>
                item.HospitalId == hospital.Id
                && item.ActorId == actorId
                && item.Capability == capability)
            .ConfigureAwait(false);
        _ = scope.DbContext.CapabilityAssignments.Remove(assignment);
        _ = await scope.DbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        string message = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Expected 403, got {(int)response.StatusCode}: {body}");
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            message);
        using var document = JsonDocument.Parse(body);
        JsonElement errors = document.RootElement.GetProperty("errors");
        JsonElement error = Assert.Single(errors.EnumerateArray());
        Assert.Equal(
            "403",
            error.GetProperty("status").GetString());
        Assert.Equal(
            "Capability required",
            error.GetProperty("title").GetString());
    }

    private async Task<int> CountAuditEventsAsync(string action)
    {
        await using AsyncServiceScope scope = Factory.Services
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .Where(item => item.Action == action)
            .CountAsync()
            .ConfigureAwait(false);
    }
}
