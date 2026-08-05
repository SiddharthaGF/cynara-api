using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests.Encounters;

/// <summary>
/// CYN-51 encounter tenant isolation tests. Confirms a secondary hospital
/// cannot list, fetch, or transition encounters owned by the primary
/// hospital, and that cross-tenant reference ids are rejected on create.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class EncountersTenantIsolationTests : IAsyncDisposable
{
    private const string ContentType = "application/vnd.api+json";
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";

    public EncountersTenantIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        PrimaryClient = Factory.CreateClient();
        PrimaryClient.AcceptJsonApi();
        PrimaryClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        PrimaryClient.DefaultRequestHeaders.Add("X-Actor-Id", "primary-clinician");

        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", OtherHospitalCode);
        OtherClient.DefaultRequestHeaders.Add("X-Actor-Id", "other-clinician");

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();
    }

    internal CynaraTenantWebApplicationFactory Factory { get; }

    public HttpClient PrimaryClient { get; }

    public HttpClient OtherClient { get; }

    public async ValueTask DisposeAsync()
    {
        PrimaryClient.Dispose();
        OtherClient.Dispose();
        await Factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task OtherHospital_CannotSeeOrTransitionPrimaryEncounter()
    {
        FixtureRefs refs = await SeedRefsAsync(PrimaryClient, "iso-primary")
            .ConfigureAwait(false);
        using JsonDocument created = await CreateEncounterAsync(
            PrimaryClient, refs).ConfigureAwait(false);
        var encounterId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage otherGet = await OtherClient
            .GetAsync(new Uri($"/api/encounters/{encounterId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);

        using HttpResponseMessage otherList = await OtherClient
            .GetAsync(new Uri(
                $"/api/encounters?patientId={refs.PatientId}",
                UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, otherList.StatusCode);
        using var listDoc = JsonDocument.Parse(
            await otherList.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(
            0,
            listDoc.RootElement.GetProperty("encounters").GetArrayLength());

        using HttpResponseMessage otherComplete = await OtherClient.SendAsync(
            PostJsonRequest(
                $"/api/encounters/{encounterId}/complete",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherComplete.StatusCode);
    }

    [Fact]
    public async Task CreateEncounter_RejectsCrossTenantPatientReference()
    {
        FixtureRefs primaryRefs = await SeedRefsAsync(
            PrimaryClient, "iso-cross-p").ConfigureAwait(false);
        FixtureRefs otherRefs = await SeedRefsAsync(
            OtherClient, "iso-cross-o").ConfigureAwait(false);

        using HttpResponseMessage rejected = await OtherClient.SendAsync(
            PostJsonRequest(
                "/api/encounters",
                new
                {
                    patientId = primaryRefs.PatientId,
                    facilityId = otherRefs.FacilityId,
                    clinicalAreaId = otherRefs.ClinicalAreaId,
                    type = "ambulatory",
                    responsibleProfessionalId = "dr-other",
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
    }

    private static async Task<FixtureRefs> SeedRefsAsync(
        HttpClient client, string suffix)
    {
        using JsonDocument facility = await PostJsonAsync(
            client,
            "/api/facilities",
            new { code = $"fac-{suffix}", name = $"Facility {suffix}" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostJsonAsync(
            client,
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
            client,
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

    private static async Task<JsonDocument> CreateEncounterAsync(
        HttpClient client, FixtureRefs refs)
    {
        using HttpResponseMessage response = await client.SendAsync(
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

    private static async Task<JsonDocument> PostJsonAsync(
        HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client
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

    private sealed record FixtureRefs(
        Guid PatientId,
        Guid FacilityId,
        Guid ClinicalAreaId);
}
