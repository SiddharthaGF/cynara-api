using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Encounters;

/// <summary>
/// CYN-51 encounter lifecycle integration tests. Covers create / get /
/// list / complete / cancel / enter-in-error, retired reference rejection,
/// optimistic concurrency, audit emission, and unauthorized access.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class EncountersLifecycleTests : IAsyncDisposable
{
    private const string ContentType = "application/vnd.api+json";
    private const string PrimaryHospitalCode = "primary";

    public EncountersLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "clinician");

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
    }

    internal CynaraTenantWebApplicationFactory Factory { get; }

    public HttpClient Client { get; }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateEncounter_PersistsAndAudits()
    {
        FixtureRefs refs = await SeedRefsAsync("life-create").ConfigureAwait(false);
        using JsonDocument created = await CreateEncounterAsync(refs)
            .ConfigureAwait(false);

        var id = Guid.Parse(created.RootElement.GetProperty("id").GetString()!);
        Assert.Equal("open", GetString(created, "status"));
        Assert.Equal("ambulatory", GetString(created, "type"));
        Assert.Equal(refs.PatientId, Guid.Parse(GetString(created, "patientId")));
        Assert.Equal(
            JsonValueKind.Null,
            created.RootElement.GetProperty("endedAt").ValueKind);

        using JsonDocument fetched = await GetEncounterAsync(id).ConfigureAwait(false);
        Assert.Equal("open", GetString(fetched, "status"));

        int auditCount = await CountAuditEventsAsync("encounter.created")
            .ConfigureAwait(false);
        Assert.True(auditCount >= 1, "Expected encounter.created audit event.");
    }

    [Fact]
    public async Task CompleteEncounter_SetsEndedAtAndRemainsQueryable()
    {
        FixtureRefs refs = await SeedRefsAsync("life-complete").ConfigureAwait(false);
        using JsonDocument created = await CreateEncounterAsync(refs)
            .ConfigureAwait(false);
        var id = Guid.Parse(created.RootElement.GetProperty("id").GetString()!);

        using JsonDocument completed = await TransitionAsync(
            id, "complete", rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", GetString(completed, "status"));
        Assert.NotEqual(
            JsonValueKind.Null,
            completed.RootElement.GetProperty("endedAt").ValueKind);
        Assert.Equal(1u, GetUInt32(completed, "rowVersion"));

        using JsonDocument fetched = await GetEncounterAsync(id).ConfigureAwait(false);
        Assert.Equal("completed", GetString(fetched, "status"));

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri(
                $"/api/encounters?patientId={refs.PatientId}",
                UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDoc = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Contains(
            id.ToString(),
            listDoc.RootElement.GetRawText(),
            StringComparison.Ordinal);

        int auditCount = await CountAuditEventsAsync("encounter.completed")
            .ConfigureAwait(false);
        Assert.True(auditCount >= 1);
    }

    [Fact]
    public async Task CancelAndEnterInError_RejectSecondTransition()
    {
        FixtureRefs refs = await SeedRefsAsync("life-cancel").ConfigureAwait(false);
        using JsonDocument created = await CreateEncounterAsync(refs)
            .ConfigureAwait(false);
        var id = Guid.Parse(created.RootElement.GetProperty("id").GetString()!);

        using JsonDocument canceled = await TransitionAsync(
            id, "cancel", rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("canceled", GetString(canceled, "status"));

        using HttpResponseMessage second = await SendTransitionAsync(
            id, "complete", rowVersion: 1).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        FixtureRefs refs2 = await SeedRefsAsync("life-error").ConfigureAwait(false);
        using JsonDocument created2 = await CreateEncounterAsync(refs2)
            .ConfigureAwait(false);
        var id2 = Guid.Parse(created2.RootElement.GetProperty("id").GetString()!);
        using JsonDocument errored = await TransitionAsync(
            id2, "enter-in-error", rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("enteredInError", GetString(errored, "status"));

        using JsonDocument stillVisible = await GetEncounterAsync(id2)
            .ConfigureAwait(false);
        Assert.Equal("enteredInError", GetString(stillVisible, "status"));
    }

    [Fact]
    public async Task Transition_RejectsStaleRowVersion()
    {
        FixtureRefs refs = await SeedRefsAsync("life-stale").ConfigureAwait(false);
        using JsonDocument created = await CreateEncounterAsync(refs)
            .ConfigureAwait(false);
        var id = Guid.Parse(created.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage first = await SendTransitionAsync(
            id, "complete", rowVersion: 0).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage stale = await SendTransitionAsync(
            id, "cancel", rowVersion: 0).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task CreateEncounter_RejectsRetiredFacilityAndUnknownType()
    {
        FixtureRefs refs = await SeedRefsAsync("life-retired").ConfigureAwait(false);
        using HttpResponseMessage retire = await Client.SendAsync(
            PostJsonRequest(
                $"/api/facilities/{refs.FacilityId}/retire",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, retire.StatusCode);

        using HttpResponseMessage rejected = await SendCreateAsync(refs)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        FixtureRefs refs2 = await SeedRefsAsync("life-badtype").ConfigureAwait(false);
        using HttpResponseMessage badType = await Client.SendAsync(
            PostJsonRequest(
                "/api/encounters",
                new
                {
                    patientId = refs2.PatientId,
                    facilityId = refs2.FacilityId,
                    clinicalAreaId = refs2.ClinicalAreaId,
                    type = "not-a-type",
                    responsibleProfessionalId = "dr-who",
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
    }

    [Fact]
    public async Task CreateEncounter_RejectsWithoutTenantContext()
    {
        FixtureRefs refs = await SeedRefsAsync("life-anon").ConfigureAwait(false);
        using HttpClient anonymous = Factory.CreateClient();
        anonymous.AcceptJsonApi();
        using HttpResponseMessage response = await anonymous.SendAsync(
            PostJsonRequest(
                "/api/encounters",
                new
                {
                    patientId = refs.PatientId,
                    facilityId = refs.FacilityId,
                    clinicalAreaId = refs.ClinicalAreaId,
                    type = "ambulatory",
                    responsibleProfessionalId = "dr-who",
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<FixtureRefs> SeedRefsAsync(string suffix)
    {
        using JsonDocument facility = await PostJsonAsync(
            "/api/facilities",
            new { code = $"fac-{suffix}", name = $"Facility {suffix}" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostJsonAsync(
            "/api/clinicalAreas",
            new
            {
                code = $"area-{suffix}",
                name = $"Area {suffix}",
                facilityId,
            })
            .ConfigureAwait(false);
        var clinicalAreaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);

        using JsonDocument patient = await PostJsonAsync(
            "/api/patients",
            new
            {
                mrn = $"MRN-{suffix}",
                givenName = "Ada",
                familyName = "Lovelace",
                birthDate = "1990-01-01",
                sex = "female",
            })
            .ConfigureAwait(false);
        var patientId = Guid.Parse(
            patient.RootElement.GetProperty("id").GetString()!);

        return new FixtureRefs(patientId, facilityId, clinicalAreaId);
    }

    private async Task<JsonDocument> CreateEncounterAsync(FixtureRefs refs)
    {
        using HttpResponseMessage response = await SendCreateAsync(refs)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            string body = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Create failed with {(int)response.StatusCode}: {body}"));
        }

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private Task<HttpResponseMessage> SendCreateAsync(FixtureRefs refs)
    {
        return Client.SendAsync(
            PostJsonRequest(
                "/api/encounters",
                new
                {
                    patientId = refs.PatientId,
                    facilityId = refs.FacilityId,
                    clinicalAreaId = refs.ClinicalAreaId,
                    type = "ambulatory",
                    responsibleProfessionalId = "dr-who",
                }));
    }

    private async Task<JsonDocument> TransitionAsync(
        Guid id, string action, uint rowVersion)
    {
        using HttpResponseMessage response = await SendTransitionAsync(
            id, action, rowVersion).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            string body = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Transition failed with {(int)response.StatusCode}: {body}"));
        }

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private Task<HttpResponseMessage> SendTransitionAsync(
        Guid id, string action, uint rowVersion)
    {
        return Client.SendAsync(
            PostJsonRequest(
                $"/api/encounters/{id}/{action}",
                new { rowVersion }));
    }

    private async Task<JsonDocument> GetEncounterAsync(Guid id)
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri($"/api/encounters/{id}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private async Task<JsonDocument> PostJsonAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client
            .SendAsync(PostJsonRequest(path, body))
            .ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        if ((int)response.StatusCode is < 200 or >= 300)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"POST {path} failed with {(int)response.StatusCode}: {text}"));
        }

        return JsonDocument.Parse(
            string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static HttpRequestMessage PostJsonRequest(string path, object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue(ContentType),
                },
            },
        };
    }

    private static string GetString(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetString() ?? string.Empty;
    }

    private static uint GetUInt32(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetUInt32();
    }

    private async Task<int> CountAuditEventsAsync(string action)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .Where(item => item.Action == action)
            .CountAsync()
            .ConfigureAwait(false);
    }

    private sealed record FixtureRefs(
        Guid PatientId,
        Guid FacilityId,
        Guid ClinicalAreaId);
}
