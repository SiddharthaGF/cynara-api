using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests;

/// <summary>
/// CYN-35 clinical taxonomy HTTP integration tests. Covers the public
/// facility, clinical area, and discipline endpoints, including
/// tenant-scoped isolation, lifecycle transitions, audit emission, and
/// the rejection rules described in the issue.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ClinicalTaxonomyLifecycleTests : IDisposable
{
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";
    private const string ContentType = "application/vnd.api+json";

    public ClinicalTaxonomyLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();

        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            PrimaryHospitalCode);
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            OtherHospitalCode);

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
    public async Task CreateFacility_PersistsOwnedRowAndAuditsCreation()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "north-campus",
                name = "North campus",
            }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, LastStatus);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);
        Assert.Equal("north-campus", GetString(created, "code"));
        Assert.Equal("active", GetString(created, "status"));
        Assert.Equal(0u, GetUInt32(created, "rowVersion"));

        Hospital primary;
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Factory.CreateScope();
        CynaraDbContext dbContext = scope.DbContext;
        primary = scope.LoadPrimaryHospital();
        Facility stored = await dbContext.Facilities
            .AsNoTracking()
            .SingleAsync(item => item.Id == facilityId)
            .ConfigureAwait(false);
        Assert.Equal(primary.Id, stored.HospitalId);

        AuditEvent createdEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.ResourceType == "facility"
                && item.ResourceId == facilityId
                && item.Action == "facility.created")
            .ConfigureAwait(false);
        Assert.Equal(primary.Id, createdEvent.HospitalId);
        Assert.NotNull(createdEvent.MetadataJson);
        Assert.Contains(
            "north-campus",
            createdEvent.MetadataJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateFacility_RejectsDuplicateCode()
    {
        await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "shared",
                name = "First",
            }).ConfigureAwait(false);

        using HttpResponseMessage response = await Client.SendAsync(
            PostJsonRequest(
                "/api/facilities",
                new
                {
                    code = "shared",
                    name = "Second",
                })).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("already exists", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateFacility_HonorsOptimisticConcurrency()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "east-campus",
                name = "East campus",
            }).ConfigureAwait(false);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);
        uint current = GetUInt32(created, "rowVersion");

        using HttpResponseMessage stale = await Client.SendAsync(
            PatchJsonRequest(
                $"/api/facilities/{facilityId}",
                new
                {
                    name = "Stale",
                    rowVersion = current + 999,
                })).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using JsonDocument patched = await PatchAsync(
            Client,
            $"/api/facilities/{facilityId}",
            new
            {
                name = "East campus (renamed)",
                rowVersion = current,
            }).ConfigureAwait(false);
        Assert.Equal("East campus (renamed)", GetString(patched, "name"));
        Assert.Equal(current + 1, GetUInt32(patched, "rowVersion"));
    }

    [Fact]
    public async Task RetireFacility_KeepsRowResolvable()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "south-campus",
                name = "South campus",
            }).ConfigureAwait(false);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);
        uint current = GetUInt32(created, "rowVersion");

        using JsonDocument retired = await PostJsonAsync(
            Client,
            $"/api/facilities/{facilityId}/retire",
            new
            {
                rowVersion = current,
            }).ConfigureAwait(false);
        Assert.Equal("retired", GetString(retired, "status"));
        Assert.NotEqual(JsonValueKind.Null, retired.RootElement
            .GetProperty("retiredAt").ValueKind);

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri("/api/facilities?includeRetired=true", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        string listBody = await list.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(facilityId.ToString(), listBody, StringComparison.Ordinal);

        using HttpResponseMessage defaultList = await Client
            .GetAsync(new Uri("/api/facilities", UriKind.Relative))
            .ConfigureAwait(false);
        string defaultBody = await defaultList.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(
            facilityId.ToString(),
            defaultBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateClinicalArea_RequiresActiveParentFacility()
    {
        using JsonDocument facility = await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "main-building",
                name = "Main building",
            }).ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostJsonAsync(
            Client,
            "/api/clinicalAreas",
            new
            {
                code = "outpatient",
                name = "Outpatient",
                facilityId,
            }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, LastStatus);
        var areaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);
        Assert.Equal(facilityId, Guid.Parse(
            area.RootElement.GetProperty("facilityId").GetString()!));

        using JsonDocument retiredFacility = await PostJsonAsync(
            Client,
            $"/api/facilities/{facilityId}/retire",
            new
            {
                rowVersion = GetUInt32(facility, "rowVersion"),
            }).ConfigureAwait(false);
        Assert.Equal("retired", GetString(retiredFacility, "status"));

        using HttpResponseMessage rejected = await Client.SendAsync(
            PostJsonRequest(
                "/api/clinicalAreas",
                new
                {
                    code = "cardiology",
                    name = "Cardiology",
                    facilityId,
                })).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        string body = await rejected.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("retired", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateDiscipline_AuditsAndFiltersByParent()
    {
        using JsonDocument facility = await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "tower-a",
                name = "Tower A",
            }).ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostJsonAsync(
            Client,
            "/api/clinicalAreas",
            new
            {
                code = "imaging",
                name = "Imaging",
                facilityId,
            }).ConfigureAwait(false);
        var areaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);

        using JsonDocument discipline = await PostJsonAsync(
            Client,
            "/api/disciplines",
            new
            {
                code = "radiology",
                name = "Radiology",
                clinicalAreaId = areaId,
            }).ConfigureAwait(false);
        var disciplineId = Guid.Parse(
            discipline.RootElement.GetProperty("id").GetString()!);
        Assert.Equal(areaId, Guid.Parse(
            discipline.RootElement.GetProperty("clinicalAreaId").GetString()!));

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri(
                $"/api/disciplines?clinicalAreaId={areaId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        string body = await list.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(disciplineId.ToString(), body, StringComparison.Ordinal);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        AuditEvent createdEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.ResourceType == "discipline"
                && item.ResourceId == disciplineId
                && item.Action == "discipline.created")
            .ConfigureAwait(false);
        Assert.NotNull(createdEvent.MetadataJson);
        Assert.Contains("imaging", createdEvent.MetadataJson!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTenant_Facility_IsHidden()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "tenant-only",
                name = "Tenant only",
            }).ConfigureAwait(false);
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
    public async Task UpdateClinicalArea_RejectsUnknownCodeShape()
    {
        using JsonDocument facility = await PostJsonAsync(
            Client,
            "/api/facilities",
            new
            {
                code = "main-wing",
                name = "Main wing",
            }).ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);
        using JsonDocument area = await PostJsonAsync(
            Client,
            "/api/clinicalAreas",
            new
            {
                code = "lab",
                name = "Lab",
                facilityId,
            }).ConfigureAwait(false);
        var areaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);
        uint rowVersion = GetUInt32(area, "rowVersion");

        using HttpResponseMessage empty = await Client.SendAsync(
            PatchJsonRequest(
                $"/api/clinicalAreas/{areaId}",
                new
                {
                    name = string.Empty,
                    rowVersion,
                })).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        string body = await empty.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("name is required", body, StringComparison.Ordinal);
    }

    private static string GetString(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetString() ?? string.Empty;
    }

    private static uint GetUInt32(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetUInt32();
    }

    private HttpStatusCode LastStatus { get; set; }

    private async Task<JsonDocument> PostJsonAsync(
        HttpClient client,
        string path,
        object body)
    {
        using HttpResponseMessage response = await client.SendAsync(
            PostJsonRequest(path, body)).ConfigureAwait(false);
        LastStatus = response.StatusCode;
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private async Task<JsonDocument> PatchAsync(
        HttpClient client,
        string path,
        object body)
    {
        using HttpResponseMessage response = await client.SendAsync(
            PatchJsonRequest(path, body)).ConfigureAwait(false);
        LastStatus = response.StatusCode;
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static HttpRequestMessage PostJsonRequest(string path, object body)
    {
        return new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue(ContentType),
                },
            },
        };
    }

    private static HttpRequestMessage PatchJsonRequest(string path, object body)
    {
        return new HttpRequestMessage(HttpMethod.Patch, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8)
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
