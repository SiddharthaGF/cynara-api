using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests.Patients;

/// <summary>
/// CYN-49 patient registry tenant isolation tests. Confirms that a
/// secondary hospital cannot list, fetch, patch, or soft-delete patients
/// owned by the primary hospital and that MRN collisions across hospitals
/// are allowed.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PatientsTenantIsolationTests : IAsyncDisposable
{
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";

    public PatientsTenantIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        PrimaryClient = Factory.CreateClient();
        PrimaryClient.AcceptJsonApi();
        PrimaryClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        PrimaryClient.DefaultRequestHeaders.Add("X-Actor-Id", "primary-registrar");

        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", OtherHospitalCode);
        OtherClient.DefaultRequestHeaders.Add("X-Actor-Id", "other-registrar");

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
    public async Task OtherHospital_CannotSeePrimaryPatient()
    {
        using JsonDocument primaryCreated = await CreatePatientAsync(
            PrimaryClient, "MRN-ISO-001").ConfigureAwait(false);
        var primaryId = Guid.Parse(primaryCreated.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage otherGet = await OtherClient
            .GetAsync(new Uri($"/api/patients/{primaryId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);

        using HttpResponseMessage otherSearch = await OtherClient
            .GetAsync(new Uri("/api/patients?mrn=MRN-ISO-001", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, otherSearch.StatusCode);
        using var otherSearchDoc = JsonDocument.Parse(
            await otherSearch.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(0, otherSearchDoc.RootElement.GetProperty("patients").GetArrayLength());
    }

    [Fact]
    public async Task OtherHospital_CannotPatchOrSoftDeletePrimaryPatient()
    {
        using JsonDocument primaryCreated = await CreatePatientAsync(
            PrimaryClient, "MRN-ISO-002").ConfigureAwait(false);
        var primaryId = Guid.Parse(primaryCreated.RootElement.GetProperty("id").GetString()!);

        HttpRequestMessage patchRequest = new(HttpMethod.Patch, $"/api/patients/{primaryId}")
        {
            Content = JsonContent.Create(new
            {
                givenName = "Mallory",
                familyName = "Byron",
                birthDate = "1990-01-01",
                sex = "female",
                bloodType = "o+",
                rowVersion = 0U,
            }),
        };
        patchRequest.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.api+json");
        using HttpResponseMessage otherPatch = await OtherClient
            .SendAsync(patchRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherPatch.StatusCode);

        HttpRequestMessage deleteRequest = new(
            HttpMethod.Post,
            $"/api/patients/{primaryId}/soft-delete")
        {
            Content = JsonContent.Create(new { rowVersion = 0U }),
        };
        deleteRequest.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.api+json");
        using HttpResponseMessage otherDelete = await OtherClient
            .SendAsync(deleteRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherDelete.StatusCode);
    }

    [Fact]
    public async Task Hospitals_CanShareMrnAcrossWorkspaces()
    {
        using JsonDocument primary = await CreatePatientAsync(
            PrimaryClient, "MRN-SHARED").ConfigureAwait(false);
        using JsonDocument other = await CreatePatientAsync(
            OtherClient, "MRN-SHARED").ConfigureAwait(false);

        var primaryId = Guid.Parse(primary.RootElement.GetProperty("id").GetString()!);
        var otherId = Guid.Parse(other.RootElement.GetProperty("id").GetString()!);
        Assert.NotEqual(primaryId, otherId);

        using HttpResponseMessage primarySearch = await PrimaryClient
            .GetAsync(new Uri("/api/patients?mrn=MRN-SHARED", UriKind.Relative))
            .ConfigureAwait(false);
        using var primarySearchDoc = JsonDocument.Parse(
            await primarySearch.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(1, primarySearchDoc.RootElement.GetProperty("patients").GetArrayLength());

        using HttpResponseMessage otherSearch = await OtherClient
            .GetAsync(new Uri("/api/patients?mrn=MRN-SHARED", UriKind.Relative))
            .ConfigureAwait(false);
        using var otherSearchDoc = JsonDocument.Parse(
            await otherSearch.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(1, otherSearchDoc.RootElement.GetProperty("patients").GetArrayLength());
    }

    private static async Task<JsonDocument> CreatePatientAsync(
        HttpClient client,
        string mrn)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/api/patients")
        {
            Content = JsonContent.Create(new
            {
                mrn,
                givenName = "Ada",
                familyName = "Lovelace",
                birthDate = "1990-01-01",
                sex = "female",
                bloodType = "o+",
            }),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.api+json");
        HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            string body = await response.Content
                .ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Create failed with {(int)response.StatusCode}: {body}"));
        }

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }
}
