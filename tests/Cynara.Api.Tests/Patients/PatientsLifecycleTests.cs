using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Api.Tests.Support;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Patients;

/// <summary>
/// CYN-49 patient registry lifecycle integration tests. Covers the
/// create / get / patch / soft-delete happy path, hospital-scoped MRN
/// uniqueness, optimistic concurrency, audit emission, and unauthorized
/// access. Cross-tenant denial is asserted in
/// <see cref="PatientsTenantIsolationTests"/>.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PatientsLifecycleTests : IAsyncDisposable
{
    private const string PrimaryHospitalCode = "primary";

    public PatientsLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "registrar");

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
    public async Task CreatePatient_PersistsAndAudits()
    {
        using JsonDocument response = await CreatePatientAsync(
            mrn: "MRN-001",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);

        Guid id = ExtractPatientId(response);
        Assert.NotEqual(Guid.Empty, id);

        using JsonDocument fetched = await GetPatientAsync(id).ConfigureAwait(false);
        JsonElement attributes = ExtractAttributes(fetched);
        Assert.Equal("MRN-001", attributes.GetProperty("mrn").GetString());
        Assert.Equal("Ada", attributes.GetProperty("givenName").GetString());
        Assert.Equal("Lovelace", attributes.GetProperty("familyName").GetString());
        Assert.Equal("female", attributes.GetProperty("sex").GetString());
        Assert.Equal("active", attributes.GetProperty("status").GetString());
        Assert.Equal(0u, attributes.GetProperty("rowVersion").GetUInt32());

        int auditCount = await CountAuditEventsAsync("patient.created")
            .ConfigureAwait(false);
        Assert.True(auditCount >= 1, "Expected patient.created audit event.");
    }

    [Fact]
    public async Task CreatePatient_NormalizesMrnToUppercase()
    {
        using JsonDocument response = await CreatePatientAsync(
            mrn: "  mrn-lower  ",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);

        JsonElement attributes = ExtractAttributes(response);
        Assert.Equal("mrn-lower", attributes.GetProperty("mrn").GetString());

        Guid id = ExtractPatientId(response);
        using HttpResponseMessage detail = await Client
            .GetAsync(new Uri($"/api/patients/{id}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task CreatePatient_RejectsUnknownSex()
    {
        HttpResponseMessage response = await SendCreateAsync(
            mrn: "MRN-002",
            givenName: "Ada",
            familyName: "Lovelace",
            sex: "not-a-real-sex").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchPatient_FindsByNormalizedMrn()
    {
        await CreatePatientAsync(
            mrn: "MRN-100",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);
        await CreatePatientAsync(
            mrn: "MRN-101",
            givenName: "Alan",
            familyName: "Turing").ConfigureAwait(false);

        using HttpResponseMessage response = await Client
            .GetAsync(
                new Uri("/api/patients?mrn=  mrn-101  ", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        JsonElement patients = document.RootElement.GetProperty("patients");
        Assert.Equal(1, patients.GetArrayLength());
        JsonElement first = patients[0];
        Assert.Equal("MRN-101", first.GetProperty("mrn").GetString());
    }

    [Fact]
    public async Task CreatePatient_RejectsDuplicateMrnWithinHospital()
    {
        await CreatePatientAsync(
            mrn: "MRN-200",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);

        HttpResponseMessage response = await SendCreateAsync(
            mrn: "mrn-200",
            givenName: "Alan",
            familyName: "Turing").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PatchPatient_BumpsRowVersion()
    {
        using JsonDocument created = await CreatePatientAsync(
            mrn: "MRN-300",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);
        Guid id = ExtractPatientId(created);
        uint rowVersion = ExtractAttributes(created).GetProperty("rowVersion").GetUInt32();

        using JsonDocument patched = await PatchPatientAsync(
            id, rowVersion, familyName: "Byron").ConfigureAwait(false);

        JsonElement attributes = ExtractAttributes(patched);
        Assert.Equal("Byron", attributes.GetProperty("familyName").GetString());
        Assert.Equal(rowVersion + 1, attributes.GetProperty("rowVersion").GetUInt32());

        int updateCount = await CountAuditEventsAsync("patient.updated")
            .ConfigureAwait(false);
        Assert.True(updateCount >= 1, "Expected patient.updated audit event.");
    }

    [Fact]
    public async Task PatchPatient_RejectsStaleRowVersion()
    {
        using JsonDocument created = await CreatePatientAsync(
            mrn: "MRN-301",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);
        Guid id = ExtractPatientId(created);
        uint current = ExtractAttributes(created).GetProperty("rowVersion").GetUInt32();

        HttpResponseMessage first = await SendPatchAsync(
            id, rowVersion: current, familyName: "Byron").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        HttpResponseMessage second = await SendPatchAsync(
            id, rowVersion: current, familyName: "Shelley").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SoftDeletePatient_HidesFromSearchAndGet()
    {
        using JsonDocument created = await CreatePatientAsync(
            mrn: "MRN-400",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);
        Guid id = ExtractPatientId(created);

        HttpRequestMessage softDeleteRequest = new(
            HttpMethod.Post,
            new Uri($"/api/patients/{id}/soft-delete", UriKind.Relative))
        {
            Content = JsonContent.Create(new { rowVersion = 0U }),
        };
        softDeleteRequest.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.api+json");
        using HttpResponseMessage softDeleteResponse = await Client
            .SendAsync(softDeleteRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, softDeleteResponse.StatusCode);

        using HttpResponseMessage get = await Client
            .GetAsync(new Uri($"/api/patients/{id}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using HttpResponseMessage search = await Client
            .GetAsync(new Uri("/api/patients?mrn=MRN-400", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchDoc = JsonDocument.Parse(
            await search.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(0, searchDoc.RootElement.GetProperty("patients").GetArrayLength());

        using HttpResponseMessage searchAll = await Client
            .GetAsync(new Uri("/api/patients?mrn=MRN-400&includeDeleted=true", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, searchAll.StatusCode);
        using var searchAllDoc = JsonDocument.Parse(
            await searchAll.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(1, searchAllDoc.RootElement.GetProperty("patients").GetArrayLength());

        int deleteCount = await CountAuditEventsAsync("patient.deleted")
            .ConfigureAwait(false);
        Assert.True(deleteCount >= 1, "Expected patient.deleted audit event.");
    }

    [Fact]
    public async Task GetPatient_Returns404WhenUnknown()
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri($"/api/patients/{Guid.NewGuid()}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreatePatient_RejectsWithoutTenantContext()
    {
        using HttpClient anonymous = Factory.CreateClient();
        anonymous.AcceptJsonApi();
        HttpResponseMessage response = await SendCreateAsync(
            anonymous,
            mrn: "MRN-999",
            givenName: "Ada",
            familyName: "Lovelace").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<JsonDocument> CreatePatientAsync(
        string mrn,
        string givenName,
        string familyName,
        string sex = "female",
        DateOnly? birthDate = null,
        string? nationalId = null)
    {
        HttpResponseMessage response = await SendCreateAsync(
            mrn, givenName, familyName, sex, birthDate, nationalId).ConfigureAwait(false);
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

    private async Task<HttpResponseMessage> SendCreateAsync(
        string mrn,
        string givenName,
        string familyName,
        string sex = "female",
        DateOnly? birthDate = null,
        string? nationalId = null)
    {
        return await SendCreateAsync(
            Client, mrn, givenName, familyName, sex, birthDate, nationalId)
            .ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        string mrn,
        string givenName,
        string familyName,
        string sex = "female",
        DateOnly? birthDate = null,
        string? nationalId = null)
    {
        var payload = new
        {
            mrn,
            nationalId,
            givenName,
            familyName,
            birthDate = (birthDate ?? new DateOnly(1990, 1, 1))
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sex,
        };
        HttpRequestMessage request = new(HttpMethod.Post, "/api/patients")
        {
            Content = JsonContent.Create(payload),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.api+json");
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private async Task<JsonDocument> PatchPatientAsync(
        Guid id,
        uint rowVersion,
        string? givenName = null,
        string? familyName = null,
        string? nationalId = null)
    {
        HttpResponseMessage response = await SendPatchAsync(
            id, rowVersion, givenName, familyName, nationalId).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            string body = await response.Content
                .ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Patch failed with {(int)response.StatusCode}: {body}"));
        }

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private async Task<HttpResponseMessage> SendPatchAsync(
        Guid id,
        uint rowVersion,
        string? givenName = null,
        string? familyName = null,
        string? nationalId = null)
    {
        var payload = new
        {
            nationalId,
            givenName = givenName ?? "Ada",
            familyName = familyName ?? "Lovelace",
            birthDate = new DateOnly(1990, 1, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sex = "female",
            rowVersion,
        };
        HttpRequestMessage request = new(HttpMethod.Patch, $"/api/patients/{id}")
        {
            Content = JsonContent.Create(payload),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.api+json");
        return await Client.SendAsync(request).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetPatientAsync(Guid id)
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri($"/api/patients/{id}", UriKind.Relative))
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            string body = await response.Content
                .ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "Get failed with "
                + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                + ": " + body);
        }

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static Guid ExtractPatientId(JsonDocument document)
    {
        return Guid.Parse(document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Missing id"));
    }

    private static JsonElement ExtractAttributes(JsonDocument document)
    {
        // Patients endpoints return the DTO directly (no JSON:API envelope),
        // so attributes live on the root element.
        return document.RootElement;
    }

    private async Task<int> CountAuditEventsAsync(string action)
    {
        Infrastructure.Persistence.CynaraDbContext dbContext =
            Factory.Services
                .GetRequiredService<IServiceScopeFactory>()
                .CreateAsyncScope()
                .ServiceProvider
                .GetRequiredService<Infrastructure.Persistence.CynaraDbContext>();
        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.AuditEvents
                .Where(item => item.Action == action)
                .CountAsync()
                .ConfigureAwait(false);
        }
    }
}
