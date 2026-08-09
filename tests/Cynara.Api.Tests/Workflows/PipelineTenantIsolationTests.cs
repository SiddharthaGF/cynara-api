using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// Cross-tenant isolation for workflow pipelines: one hospital cannot read,
/// list, or advance another hospital's pipelines.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PipelineTenantIsolationTests : IDisposable
{
    public PipelineTenantIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(CynaraTenantWebApplicationFactory.PrimaryCode);
        OtherApi = new JsonApiClient(OtherClient);
        OtherApi.UseHospitalContext(CynaraTenantWebApplicationFactory.OtherCode);

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
    public async Task CrossTenant_Pipeline_IsNotVisible()
    {
        await PublishWorkflowAsync(
            "isolation-pipeline",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        using JsonDocument started = await StartPipelineAsync(
            "isolation-pipeline").ConfigureAwait(false);
        string pipelineId = started.RootElement.GetProperty("id").GetString()!;

        using HttpResponseMessage get = await OtherClient.GetAsync(
            new Uri($"/api/pipelines/{pipelineId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using HttpResponseMessage list = await OtherClient.GetAsync(
            new Uri("/api/pipelines", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Empty(listBody.RootElement.GetProperty("pipelines").EnumerateArray());

        using HttpResponseMessage advance = await OtherClient.PostAsync(
            new Uri($"/api/pipelines/{pipelineId}/advance", UriKind.Relative),
            new StringContent(
                /*lang=json,strict*/ """{ "rowVersion": 0 }""",
                Encoding.UTF8,
                "application/json")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, advance.StatusCode);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    private JsonApiClient Api { get; }

    private JsonApiClient OtherApi { get; }

    private async Task PublishWorkflowAsync(string code, string workflowSchemaJson)
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code,
                name = code,
                initialWorkflowSchemaJson = workflowSchemaJson,
            }).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        using JsonDocument definition = await Api.GetAsync(
            $"/api/workflowDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        string draftId = definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes").GetProperty("status").GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;

        uint rowVersion = await GetRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument inReview = await PostVersionActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        _ = await PostVersionActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
    }

    private async Task<uint> GetRowVersionAsync(string versionId)
    {
        using JsonDocument document = await Api.GetAsync(
            $"/api/workflowVersions/{versionId}").ConfigureAwait(false);
        return JsonApiClient.AttrUInt(document, "rowVersion");
    }

    private async Task<JsonDocument> PostVersionActionAsync(
        string versionId,
        string action,
        uint? rowVersion)
    {
        string suffix = rowVersion is null
            ? string.Empty
            : $"?rowVersion={rowVersion.Value.ToString(CultureInfo.InvariantCulture)}";
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri(
                $"/api/workflowVersions/{versionId}/{action}{suffix}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        return await JsonApiClient.ReadDocumentAsync(response).ConfigureAwait(false);
    }

    private async Task<JsonDocument> StartPipelineAsync(string workflowCode)
    {
        Guid encounterId = await SeedEncounterAsync().ConfigureAwait(false);
        var payload = new
        {
            workflowCode,
            subjectType = "encounter",
            subjectId = encounterId,
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/pipelines", UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };
        using HttpResponseMessage response = await Client.SendAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private async Task<Guid> SeedEncounterAsync()
    {
        Guid facilityId = await CreatePlainAsync(
            "/api/facilities",
            new
            {
                code = $"fac-{Guid.NewGuid():N}",
                name = "Facility",
            }).ConfigureAwait(false);
        Guid clinicalAreaId = await CreatePlainAsync(
            "/api/clinicalAreas",
            new
            {
                code = $"area-{Guid.NewGuid():N}",
                name = "Area",
                facilityId,
            }).ConfigureAwait(false);
        Guid patientId = await CreatePlainAsync(
            "/api/patients",
            new
            {
                mrn = $"MRN-{Guid.NewGuid():N}",
                nationalId = (string?)null,
                givenName = "Ada",
                familyName = "Lovelace",
                birthDate = "1990-01-01",
                sex = "female",
                bloodType = "o+",
            }).ConfigureAwait(false);
        return await CreatePlainAsync(
            "/api/encounters",
            new
            {
                patientId,
                facilityId,
                clinicalAreaId,
                type = "ambulatory",
                responsibleProfessionalId = "dr-who",
            }).ConfigureAwait(false);
    }

    private async Task<Guid> CreatePlainAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri(path, UriKind.Relative),
            new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                JsonApiMedia.ContentType)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return Guid.Parse(
            document.RootElement.GetProperty("id").GetString()!,
            CultureInfo.InvariantCulture);
    }
}
