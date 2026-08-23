using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Documents;

/// <summary>
/// CYN-36 clinical document catalog lifecycle integration tests.
/// Covers CRUD, optimistic concurrency, retirement, and audit emission
/// for the document catalog endpoints.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class DocumentCatalogLifecycleTests : IDisposable
{
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";

    public DocumentCatalogLifecycleTests(
        PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();

        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "catalog-admin");
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", OtherHospitalCode);

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();

        Api = new JsonApiClient(Client);
        Workflow = new JsonApiWorkflow(Api, Client);
    }

    public void Dispose()
    {
        Client.Dispose();
        OtherClient.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The create response only exposes relationship links; JSON:API populates
    /// `data` only when the relationship is explicitly included, so re-fetch
    /// with `include=` to get the FKs in the body.
    /// </summary>
    [Fact]
    public async Task CreateDocumentDefinition_PersistsAndAudits()
    {
        DocumentCatalogFixture fixture = await SeedFixtureAsync(
            "north-campus", "outpatient", "nutrition", "initial-nutrition-assessment")
            .ConfigureAwait(false);

        using JsonDocument response = await Api.PostResourceAsync(
            "documentDefinitions",
            new
            {
                code = "nutritional-assessment",
                name = "Initial Nutritional Assessment",
                allowsMultipleInstancesPerEncounter = true,
                requiresActorForCreation = true,
                requiresActorForCompletion = true,
            },
            DocumentRelationships(fixture)).ConfigureAwait(false);

        Assert.True(response.RootElement.TryGetProperty("data", out _));
        JsonElement data = response.RootElement.GetProperty("data");
        var documentDefinitionId = Guid.Parse(
            data.GetProperty("id").GetString()!);
        JsonElement attributes = data.GetProperty("attributes");
        Assert.Equal("nutritional-assessment", attributes.GetProperty("code").GetString());
        Assert.Equal("active", attributes.GetProperty("status").GetString());
        Assert.Equal(0u, attributes.GetProperty("rowVersion").GetUInt32());

        using JsonDocument included = await Api.GetAsync(
            $"/api/documentDefinitions/{documentDefinitionId}"
                + "?include=formDefinition,formVersion,facility,clinicalArea,discipline")
            .ConfigureAwait(false);
        JsonElement includedData = included.RootElement.GetProperty("data");
        Assert.True(
            includedData.TryGetProperty("relationships", out JsonElement relationships),
            $"Missing relationships in response: {includedData}");
        Assert.True(
            relationships.TryGetProperty("formVersion", out _),
            $"Missing formVersion relationship: {relationships}");
        Assert.Equal(
            fixture.FormVersionId,
            Guid.Parse(relationships.GetProperty("formVersion")
                .GetProperty("data").GetProperty("id").GetString()!));
        Assert.Equal(
            fixture.FacilityId,
            Guid.Parse(relationships.GetProperty("facility")
                .GetProperty("data").GetProperty("id").GetString()!));
        Assert.Equal(
            fixture.ClinicalAreaId,
            Guid.Parse(relationships.GetProperty("clinicalArea")
                .GetProperty("data").GetProperty("id").GetString()!));
        Assert.Equal(
            fixture.DisciplineId,
            Guid.Parse(relationships.GetProperty("discipline")
                .GetProperty("data").GetProperty("id").GetString()!));

        await using AsyncServiceScope scope = Factory.Services
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        AuditEvent createdEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.ResourceType == "document-definition"
                && item.ResourceId == documentDefinitionId
                && item.Action == "document-definition.created")
            .ConfigureAwait(false);
        Assert.Equal("catalog-admin", createdEvent.ActorId);
        Assert.Contains(
            "nutritional-assessment",
            createdEvent.MetadataJson ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDocumentDefinition_RequiresPublishedFormVersion()
    {
        DocumentCatalogFixture fixture = await SeedFixtureAsync(
            "draft-ward", "draft-area", "draft-discipline", "draft-form")
            .ConfigureAwait(false);

        using HttpResponseMessage response = await Client.PostAsync(
            new Uri("/api/documentDefinitions", UriKind.Relative),
            MakeJsonApiContent(
                new
                {
                    data = new
                    {
                        type = "documentDefinitions",
                        attributes = new
                        {
                            code = "draft-bound",
                            name = "Draft-bound document",
                            allowsMultipleInstancesPerEncounter = true,
                            requiresActorForCreation = true,
                            requiresActorForCompletion = true,
                        },
                        relationships = DocumentRelationships(fixture, useDraftVersion: true),
                    },
                })).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("not published", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDocumentDefinition_RequiresDisciplineUnderClinicalArea()
    {
        DocumentCatalogFixture first = await SeedFixtureAsync(
            "alpha-ward", "alpha-area", "alpha-discipline", "alpha-form")
            .ConfigureAwait(false);
        DocumentCatalogFixture second = await SeedFixtureAsync(
            "beta-ward", "beta-area", "beta-discipline", "beta-form")
            .ConfigureAwait(false);

        object firstRelationships = DocumentRelationships(first);
        Dictionary<string, object> mixedRelationships = new(
            StringComparer.Ordinal);
        foreach (System.Reflection.PropertyInfo property in firstRelationships
            .GetType().GetProperties())
        {
            mixedRelationships[property.Name] = property.GetValue(firstRelationships)!;
        }

        mixedRelationships["discipline"] = new
        {
            data = new { type = "disciplines", id = second.DisciplineId },
        };

        using HttpResponseMessage response = await Client.PostAsync(
            new Uri("/api/documentDefinitions", UriKind.Relative),
            MakeJsonApiContent(
                new
                {
                    data = new
                    {
                        type = "documentDefinitions",
                        attributes = new
                        {
                            code = "wrong-discipline",
                            name = "Wrong discipline",
                            allowsMultipleInstancesPerEncounter = true,
                            requiresActorForCreation = true,
                            requiresActorForCompletion = true,
                        },
                        relationships = mixedRelationships,
                    },
                })).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            "does not belong to", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateDocumentDefinition_RequiresCurrentRowVersion()
    {
        DocumentCatalogFixture fixture = await SeedFixtureAsync(
            "concur-ward", "concur-area", "concur-discipline", "concur-form")
            .ConfigureAwait(false);

        using JsonDocument created = await Api.PostResourceAsync(
            "documentDefinitions",
            new
            {
                code = "concurrent-document",
                name = "Concurrent document",
            },
            DocumentRelationships(fixture)).ConfigureAwait(false);

        var documentDefinitionId = Guid.Parse(
            created.RootElement.GetProperty("data").GetProperty("id").GetString()!);

        using HttpResponseMessage stale = await Client.SendAsync(
            MakeJsonApiRequest(
                HttpMethod.Patch,
                $"/api/documentDefinitions/{documentDefinitionId}",
                new
                {
                    data = new
                    {
                        type = "documentDefinitions",
                        id = documentDefinitionId,
                        attributes = new
                        {
                            name = "Updated name",
                            allowsMultipleInstancesPerEncounter = false,
                            requiresActorForCreation = true,
                            requiresActorForCompletion = false,
                            rowVersion = 99u,
                        },
                    },
                })).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using HttpResponseMessage ok = await Client.SendAsync(
            MakeJsonApiRequest(
                HttpMethod.Patch,
                $"/api/documentDefinitions/{documentDefinitionId}",
                new
                {
                    data = new
                    {
                        type = "documentDefinitions",
                        id = documentDefinitionId,
                        attributes = new
                        {
                            name = "Updated name",
                            allowsMultipleInstancesPerEncounter = false,
                            requiresActorForCreation = true,
                            requiresActorForCompletion = false,
                            rowVersion = 0u,
                        },
                    },
                })).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        string responseBody = await ok.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        using var updated = JsonDocument.Parse(responseBody);
        Assert.Equal(
            "Updated name",
            updated.RootElement.GetProperty("data").GetProperty("attributes")
                .GetProperty("name").GetString());
        Assert.Equal(
            1u,
            updated.RootElement.GetProperty("data").GetProperty("attributes")
                .GetProperty("rowVersion").GetUInt32());
    }

    [Fact]
    public async Task RetireDocumentDefinition_PreservesFormVersionSnapshot()
    {
        DocumentCatalogFixture fixture = await SeedFixtureAsync(
            "retire-ward", "retire-area", "retire-discipline", "retire-form")
            .ConfigureAwait(false);

        using JsonDocument created = await Api.PostResourceAsync(
            "documentDefinitions",
            new
            {
                code = "retire-me",
                name = "Retire me",
            },
            DocumentRelationships(fixture)).ConfigureAwait(false);
        var documentDefinitionId = Guid.Parse(
            created.RootElement.GetProperty("data").GetProperty("id").GetString()!);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            new Uri(
                $"/api/documentDefinitions/{documentDefinitionId}/retire?rowVersion=0",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, retireResponse.StatusCode);
        using var retired = JsonDocument.Parse(
            await retireResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(
            "retired",
            retired.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("status")
                .GetString());

        using HttpResponseMessage listNoRetired = await Client
            .GetAsync(new Uri("/api/documentDefinitions", UriKind.Relative))
            .ConfigureAwait(false);
        string noRetiredBody = await listNoRetired.Content
            .ReadAsStringAsync().ConfigureAwait(false);
        Assert.DoesNotContain(
            documentDefinitionId.ToString(),
            noRetiredBody,
            StringComparison.Ordinal);

        using HttpResponseMessage listRetired = await Client
            .GetAsync(new Uri(
                "/api/documentDefinitions?includeRetired=true",
                UriKind.Relative))
            .ConfigureAwait(false);
        string retiredBody = await listRetired.Content
            .ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains(
            documentDefinitionId.ToString(),
            retiredBody,
            StringComparison.Ordinal);

        using HttpResponseMessage duplicateRetire = await Client.PostAsync(
            new Uri(
                $"/api/documentDefinitions/{documentDefinitionId}/retire?rowVersion=1",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, duplicateRetire.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_DocumentDefinition_IsHidden()
    {
        DocumentCatalogFixture fixture = await SeedFixtureAsync(
            "tenant-ward", "tenant-area", "tenant-discipline", "tenant-form")
            .ConfigureAwait(false);

        using JsonDocument created = await Api.PostResourceAsync(
            "documentDefinitions",
            new
            {
                code = "tenant-only",
                name = "Tenant only",
            },
            DocumentRelationships(fixture)).ConfigureAwait(false);
        var documentDefinitionId = Guid.Parse(
            created.RootElement.GetProperty("data").GetProperty("id").GetString()!);

        using HttpResponseMessage otherList = await OtherClient
            .GetAsync(new Uri(
                "/api/documentDefinitions?includeRetired=true",
                UriKind.Relative))
            .ConfigureAwait(false);
        string body = await otherList.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.DoesNotContain(
            documentDefinitionId.ToString(),
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDocumentDefinition_DuplicateCode_ReturnsConflict()
    {
        DocumentCatalogFixture fixture = await SeedFixtureAsync(
            "dup-ward", "dup-area", "dup-discipline", "dup-form")
            .ConfigureAwait(false);

        using JsonDocument created = await Api.PostResourceAsync(
            "documentDefinitions",
            new
            {
                code = "duplicate-code",
                name = "First document",
            },
            DocumentRelationships(fixture)).ConfigureAwait(false);
        Assert.NotEqual(JsonValueKind.Null, created.RootElement
            .GetProperty("data").GetProperty("id").ValueKind);

        using HttpResponseMessage conflict = await Client.PostAsync(
            new Uri("/api/documentDefinitions", UriKind.Relative),
            MakeJsonApiContent(
                new
                {
                    data = new
                    {
                        type = "documentDefinitions",
                        attributes = new
                        {
                            code = "duplicate-code",
                            name = "Second document",
                            allowsMultipleInstancesPerEncounter = true,
                            requiresActorForCreation = true,
                            requiresActorForCompletion = true,
                        },
                        relationships = DocumentRelationships(fixture),
                    },
                })).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    private async Task<DocumentCatalogFixture> SeedFixtureAsync(
        string facilityCode,
        string areaCode,
        string disciplineCode,
        string formCode)
    {
        using JsonDocument facility = await PostRawJsonAsync(
            "facilities",
            new { code = facilityCode, name = facilityCode }).ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostRawJsonAsync(
            "clinicalAreas",
            new { code = areaCode, name = areaCode, facilityId })
            .ConfigureAwait(false);
        var clinicalAreaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);

        using JsonDocument discipline = await PostRawJsonAsync(
            "disciplines",
            new { code = disciplineCode, name = disciplineCode, clinicalAreaId })
            .ConfigureAwait(false);
        var disciplineId = Guid.Parse(
            discipline.RootElement.GetProperty("id").GetString()!);

        (string definitionId, string formVersionId) = await Workflow
            .PublishFormAsync(
                formCode,
                formCode,
                JsonApiWorkflow.MinimalClinicalSchema(
                    "field-" + formCode,
                    "code-" + formCode))
            .ConfigureAwait(false);

        using JsonDocument draftFormDefinition = await Api.PostResourceAsync(
            "formDefinitions",
            new
            {
                code = formCode + "-unpublished",
                name = formCode + "-unpublished",
                initialClinicalSchemaJson = JsonApiWorkflow.MinimalClinicalSchema(
                    "field-" + formCode + "-unpublished",
                    "code-" + formCode + "-unpublished"),
            }).ConfigureAwait(false);
        string draftDefinitionId = JsonApiClient.RequireId(draftFormDefinition);
        string draftFormVersionId = await Workflow
            .GetFormDraftIdAsync(draftDefinitionId)
            .ConfigureAwait(false);

        return new DocumentCatalogFixture(
            facilityId,
            clinicalAreaId,
            disciplineId,
            Guid.Parse(definitionId),
            Guid.Parse(formVersionId),
            Guid.Parse(draftFormVersionId));
    }

    private async Task<JsonDocument> PostRawJsonAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri($"/api/{path}", UriKind.Relative),
            new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/vnd.api+json")).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"HTTP {(int)response.StatusCode}: {text}"));
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static StringContent MakeJsonApiContent(object payload)
    {
        return JsonApiClient.CreateJsonApiContent(payload);
    }

    private static HttpRequestMessage MakeJsonApiRequest(
        HttpMethod method, string path, object payload)
    {
        return new HttpRequestMessage(
            method, new Uri(path, UriKind.Relative))
        {
            Content = JsonApiClient.CreateJsonApiContent(payload),
        };
    }

    private static object DocumentRelationships(
        DocumentCatalogFixture fixture,
        bool useDraftVersion = false)
    {
        return new
        {
            formDefinition = new
            {
                data = new { type = "formDefinitions", id = fixture.FormDefinitionId },
            },
            formVersion = new
            {
                data = new
                {
                    type = "formVersions",
                    id = useDraftVersion
                        ? fixture.DraftFormVersionId
                        : fixture.FormVersionId,
                },
            },
            facility = new
            {
                data = new { type = "facilities", id = fixture.FacilityId },
            },
            clinicalArea = new
            {
                data = new { type = "clinicalAreas", id = fixture.ClinicalAreaId },
            },
            discipline = new
            {
                data = new { type = "disciplines", id = fixture.DisciplineId },
            },
        };
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private sealed record DocumentCatalogFixture(
        Guid FacilityId,
        Guid ClinicalAreaId,
        Guid DisciplineId,
        Guid FormDefinitionId,
        Guid FormVersionId,
        Guid DraftFormVersionId);
}
