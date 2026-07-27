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

namespace Cynara.Api.Tests;

/// <summary>
/// CYN-35 facility lifecycle integration tests. Covers CRUD, optimistic
/// concurrency, retirement, and audit emission for facility endpoints.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class FacilityTaxonomyLifecycleTests : IDisposable
{
    private const string ContentType = "application/vnd.api+json";

    public FacilityTaxonomyLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", "primary");

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateFacility_PersistsOwnedRowAndAuditsCreation()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "north-campus", name = "North campus" })
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, LastStatus);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);
        Assert.Equal("north-campus", GetString(created, "code"));
        Assert.Equal("active", GetString(created, "status"));
        Assert.Equal(0u, GetUInt32(created, "rowVersion"));

        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Factory.CreateScope();
        CynaraDbContext dbContext = scope.DbContext;
        Hospital primary = scope.LoadPrimaryHospital();
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
            new { code = "shared", name = "First" })
            .ConfigureAwait(false);

        using HttpResponseMessage response = await Client.SendAsync(
            PostJsonRequest(
                "/api/facilities",
                new { code = "shared", name = "Second" }))
            .ConfigureAwait(false);
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
            new { code = "east-campus", name = "East campus" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);
        uint current = GetUInt32(created, "rowVersion");

        using HttpResponseMessage stale = await Client.SendAsync(
            PatchJsonRequest(
                $"/api/facilities/{facilityId}",
                new { name = "Stale", rowVersion = current + 999 }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using JsonDocument patched = await PatchAsync(
            Client,
            $"/api/facilities/{facilityId}",
            new { name = "East campus (renamed)", rowVersion = current })
            .ConfigureAwait(false);
        Assert.Equal("East campus (renamed)", GetString(patched, "name"));
        Assert.Equal(current + 1, GetUInt32(patched, "rowVersion"));
    }

    [Fact]
    public async Task RetireFacility_KeepsRowResolvable()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "south-campus", name = "South campus" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);
        uint current = GetUInt32(created, "rowVersion");

        using JsonDocument retired = await PostJsonAsync(
            Client,
            $"/api/facilities/{facilityId}/retire",
            new { rowVersion = current })
            .ConfigureAwait(false);
        Assert.Equal("retired", GetString(retired, "status"));
        Assert.NotEqual(JsonValueKind.Null, retired.RootElement
            .GetProperty("retiredAt").ValueKind);

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri(
                "/api/facilities?includeRetired=true", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        string listBody = await list.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            facilityId.ToString(),
            listBody,
            StringComparison.Ordinal);

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
    public async Task ListFacilities_ExcludesRetiredByDefault()
    {
        using JsonDocument created = await PostJsonAsync(
            Client,
            "/api/facilities",
            new { code = "temp-site", name = "Temp site" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);
        uint current = GetUInt32(created, "rowVersion");

        await PostJsonAsync(
            Client,
            $"/api/facilities/{facilityId}/retire",
            new { rowVersion = current })
            .ConfigureAwait(false);

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri("/api/facilities", UriKind.Relative))
            .ConfigureAwait(false);
        string body = await list.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(
            facilityId.ToString(),
            body,
            StringComparison.Ordinal);

        using HttpResponseMessage includeList = await Client
            .GetAsync(new Uri(
                "/api/facilities?includeRetired=true", UriKind.Relative))
            .ConfigureAwait(false);
        string includeBody = await includeList.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            facilityId.ToString(),
            includeBody,
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

    private async Task<JsonDocument> PatchAsync(
        HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.SendAsync(
            PatchJsonRequest(path, body)).ConfigureAwait(false);
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
}
