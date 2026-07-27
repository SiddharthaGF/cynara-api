using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests;

/// <summary>
/// CYN-35 clinical-area and discipline lifecycle integration tests.
/// Covers CRUD, parent-child constraints, audit emission, and
/// cross-tenant isolation for clinical-area and discipline endpoints.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ClinicalAreaDisciplineTaxonomyLifecycleTests : IDisposable
{
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";
    private const string ContentType = "application/vnd.api+json";

    public ClinicalAreaDisciplineTaxonomyLifecycleTests(
        PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();

        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", OtherHospitalCode);

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Client.Dispose();
        OtherClient.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateClinicalArea_RequiresActiveParentFacility()
    {
        using JsonDocument facility = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "main-building", name = "Main building" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostJsonAsync(
            Client,
            "/api/clinicalAreas",
            new { code = "outpatient", name = "Outpatient", facilityId })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, LastStatus);
        var areaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);
        Assert.Equal(facilityId, Guid.Parse(
            area.RootElement.GetProperty("facilityId").GetString()!));

        using JsonDocument retiredFacility = await PostJsonAsync(
            Client,
            $"/api/facilities/{facilityId}/retire",
            new { rowVersion = GetUInt32(facility, "rowVersion") })
            .ConfigureAwait(false);
        Assert.Equal("retired", GetString(retiredFacility, "status"));

        using HttpResponseMessage rejected = await Client.SendAsync(
            PostJsonRequest(
                "/api/clinicalAreas",
                new
                {
                    code = "cardiology",
                    name = "Cardiology",
                    facilityId,
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        string body = await rejected.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("retired", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateClinicalArea_RejectsUnknownCodeShape()
    {
        using JsonDocument facility = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "main-wing", name = "Main wing" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);
        using JsonDocument area = await PostJsonAsync(
            Client,
            "/api/clinicalAreas",
            new { code = "lab", name = "Lab", facilityId })
            .ConfigureAwait(false);
        var areaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);
        uint rowVersion = GetUInt32(area, "rowVersion");

        using HttpResponseMessage empty = await Client.SendAsync(
            PatchJsonRequest(
                $"/api/clinicalAreas/{areaId}",
                new { name = string.Empty, rowVersion }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        string body = await empty.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            "name is required", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDiscipline_AuditsAndFiltersByParent()
    {
        using JsonDocument facility = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "tower-a", name = "Tower A" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostJsonAsync(
            Client,
            "/api/clinicalAreas",
            new { code = "imaging", name = "Imaging", facilityId })
            .ConfigureAwait(false);
        var areaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);

        using JsonDocument discipline = await PostJsonAsync(
            Client,
            "/api/disciplines",
            new { code = "radiology", name = "Radiology", clinicalAreaId = areaId })
            .ConfigureAwait(false);
        var disciplineId = Guid.Parse(
            discipline.RootElement.GetProperty("id").GetString()!);
        Assert.Equal(areaId, Guid.Parse(
            discipline.RootElement.GetProperty("clinicalAreaId").GetString()!));

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri(
                $"/api/disciplines?clinicalAreaId={areaId}",
                UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        string body = await list.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            disciplineId.ToString(),
            body,
            StringComparison.Ordinal);

        await using AsyncServiceScope scope =
            Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        AuditEvent createdEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.ResourceType == "discipline"
                && item.ResourceId == disciplineId
                && item.Action == "discipline.created")
            .ConfigureAwait(false);
        string metadata = createdEvent.MetadataJson
            ?? throw new InvalidOperationException(
                "Audit metadata should have been populated.");
        Assert.Contains("imaging", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTenant_Facility_IsHidden()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "tenant-only", name = "Tenant only" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage primaryList = await Client
            .GetAsync(new Uri("/api/facilities", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, primaryList.StatusCode);
        string primaryBody = await primaryList.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            facilityId.ToString(),
            primaryBody,
            StringComparison.Ordinal);

        using HttpResponseMessage otherList = await OtherClient
            .GetAsync(new Uri("/api/facilities", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, otherList.StatusCode);
        string otherBody = await otherList.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(
            facilityId.ToString(),
            otherBody,
            StringComparison.Ordinal);

        using HttpResponseMessage otherRead = await OtherClient
            .GetAsync(new Uri(
                "/api/facilities?includeRetired=true", UriKind.Relative))
            .ConfigureAwait(false);
        string otherReadBody = await otherRead.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(
            facilityId.ToString(),
            otherReadBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTenant_ClinicalArea_IsHidden()
    {
        using JsonDocument facility = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "iso-building", name = "ISO building" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostJsonAsync(
            Client,
            "/api/clinicalAreas",
            new { code = "iso-ward", name = "ISO ward", facilityId })
            .ConfigureAwait(false);
        var areaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage primaryList = await Client
            .GetAsync(new Uri("/api/clinicalAreas", UriKind.Relative))
            .ConfigureAwait(false);
        string primaryBody = await primaryList.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            areaId.ToString(),
            primaryBody,
            StringComparison.Ordinal);

        using HttpResponseMessage otherList = await OtherClient
            .GetAsync(new Uri("/api/clinicalAreas", UriKind.Relative))
            .ConfigureAwait(false);
        string otherBody = await otherList.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(
            areaId.ToString(),
            otherBody,
            StringComparison.Ordinal);
    }

    private static string GetString(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetString()
            ?? string.Empty;
    }

    private static uint GetUInt32(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetUInt32();
    }

    private HttpStatusCode LastStatus { get; set; }

    private async Task<JsonDocument> PostJsonAsync(
        HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.SendAsync(
            PostJsonRequest(path, body)).ConfigureAwait(false);
        LastStatus = response.StatusCode;
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(
            string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static HttpRequestMessage PostJsonRequest(
        string path, object body)
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

    private static HttpRequestMessage PatchJsonRequest(
        string path, object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Patch, new Uri(path, UriKind.Relative))
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

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }
}
