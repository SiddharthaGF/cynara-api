using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// HTTP-level tests for patient and encounter pipeline journeys: journeys
/// render the exact published workflow graph pinned at start time with the
/// immutable progression history, and are never rewritten when newer
/// workflow versions are published mid-flight.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PipelineJourneyTests : IDisposable
{
    public PipelineJourneyTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new WorkflowTestApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(Factory.BootstrapOptions.BootstrapCode);
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PatientJourney_ListsAllPipelines_WithGraphProjectionAndOrderedHistory()
    {
        await PublishWorkflowAsync(
            "journey-pat",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(patientId).ConfigureAwait(false);

        Guid patientPipeline = await StartPipelineIdAsync(
            "journey-pat",
            subjectType: "patient",
            subjectId: patientId).ConfigureAwait(false);
        Guid encounterPipeline = await StartPipelineIdAsync(
            "journey-pat",
            subjectType: "encounter",
            subjectId: encounterId).ConfigureAwait(false);

        using JsonDocument response = await GetPatientJourneyAsync(patientId)
            .ConfigureAwait(false);
        Assert.Equal(
            patientId.ToString("D", CultureInfo.InvariantCulture),
            Str(response, "patientId"));

        JsonElement[] journeys = [.. response.RootElement
            .GetProperty("journeys")
            .EnumerateArray()];
        Assert.Equal(2, journeys.Length);

        // Latest start first: the encounter-bound pipeline.
        JsonElement encounterJourney = journeys[0];
        Assert.Equal(
            encounterPipeline.ToString("D", CultureInfo.InvariantCulture),
            Str(encounterJourney, "pipelineId"));
        Assert.Equal("encounter", Str(encounterJourney, "subjectType"));
        Assert.Equal(
            encounterId.ToString("D", CultureInfo.InvariantCulture),
            Str(encounterJourney, "subjectId"));
        Assert.Equal(
            patientId.ToString("D", CultureInfo.InvariantCulture),
            Str(encounterJourney, "patientId"));
        Assert.Equal(
            encounterId.ToString("D", CultureInfo.InvariantCulture),
            Str(encounterJourney, "encounterId"));

        JsonElement patientJourney = journeys[1];
        Assert.Equal(
            patientPipeline.ToString("D", CultureInfo.InvariantCulture),
            Str(patientJourney, "pipelineId"));
        Assert.Equal("patient", Str(patientJourney, "subjectType"));
        Assert.Equal(
            patientId.ToString("D", CultureInfo.InvariantCulture),
            Str(patientJourney, "subjectId"));
        Assert.Equal(
            patientId.ToString("D", CultureInfo.InvariantCulture),
            Str(patientJourney, "patientId"));
        Assert.Null(GetOrNull(patientJourney, "encounterId"));

        JsonElement graph = patientJourney.GetProperty("graph");
        string[] nodeIds = [.. graph.GetProperty("nodes").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)];
        Assert.Equal(["start", "end"], nodeIds);
        JsonElement edge = graph.GetProperty("edges").EnumerateArray().Single();
        Assert.Equal("start", Str(edge, "from"));
        Assert.Equal("end", Str(edge, "to"));
        Assert.Equal("Begin", Str(edge, "label"));

        JsonElement[] history = [.. patientJourney.GetProperty("history")
            .EnumerateArray()];
        Assert.Single(history);
        Assert.Equal(1, history[0].GetProperty("sequence").GetInt32());
        Assert.Equal("pipeline.started", Str(history[0], "action"));
    }

    [Fact]
    public async Task EncounterJourney_ReturnsPinnedGraph_AndCurrentState()
    {
        await PublishWorkflowAsync(
            "journey-enc",
            WorkflowTestSchemas.WithDecision()).ConfigureAwait(false);
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(patientId).ConfigureAwait(false);

        Guid pipelineId = await StartPipelineIdAsync(
            "journey-enc",
            subjectType: "encounter",
            subjectId: encounterId).ConfigureAwait(false);
        using JsonDocument advanced = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("triage", Str(advanced, "currentNodeId"));

        using JsonDocument response = await GetEncounterJourneyAsync(encounterId)
            .ConfigureAwait(false);
        Assert.Equal(
            encounterId.ToString("D", CultureInfo.InvariantCulture),
            Str(response, "encounterId"));

        JsonElement journey = response.RootElement
            .GetProperty("journeys")
            .EnumerateArray()
            .Single();
        Assert.Equal(
            pipelineId.ToString("D", CultureInfo.InvariantCulture),
            Str(journey, "pipelineId"));
        Assert.Equal("journey-enc", Str(journey, "workflowCode"));
        Assert.Equal("1.0.0", Str(journey, "workflowVersion"));
        Assert.Equal("running", Str(journey, "status"));
        Assert.Equal("triage", Str(journey, "currentNodeId"));

        JsonElement graph = journey.GetProperty("graph");
        string[] nodeIds = [.. graph.GetProperty("nodes").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)];
        Assert.Equal(
            ["start", "triage", "low-task", "high-task", "end"],
            nodeIds);
        Assert.Equal(5, graph.GetProperty("edges").GetArrayLength());

        int[] sequences = [.. journey.GetProperty("history").EnumerateArray()
            .Select(item => item.GetProperty("sequence").GetInt32())];
        Assert.Equal([1, 2], sequences);
    }

    [Fact]
    public async Task Journey_RendersPinnedVersionGraph_NeverRewrittenInFlight()
    {
        string pinnedVersionId = await PublishWorkflowAsync(
            "journey-hist",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(patientId).ConfigureAwait(false);

        Guid pipelineId = await StartPipelineIdAsync(
            "journey-hist",
            subjectType: "encounter",
            subjectId: encounterId).ConfigureAwait(false);

        // Publish a newer graph while the v1.0.0 pipeline is still running.
        await PublishNextVersionWithSchemaAsync(
            "journey-hist",
            NextVersionSchema()).ConfigureAwait(false);
        _ = await CompletePipelineAsync(pipelineId, rowVersion: 0)
            .ConfigureAwait(false);

        using JsonDocument response = await GetPatientJourneyAsync(patientId)
            .ConfigureAwait(false);
        JsonElement journey = response.RootElement
            .GetProperty("journeys")
            .EnumerateArray()
            .Single();
        Assert.Equal("1.0.0", Str(journey, "workflowVersion"));
        Assert.Equal(pinnedVersionId, Str(journey, "workflowVersionId"));
        Assert.Equal("completed", Str(journey, "status"));

        // The journey still renders the 1.0.0 graph (start + end), never the
        // 1.0.1 graph with the extra "collect" node.
        string[] nodeIds = [.. journey.GetProperty("graph").GetProperty("nodes")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)];
        Assert.Equal(["start", "end"], nodeIds);
    }

    [Fact]
    public async Task CompletedJourney_ShowsFullImmutableProgression()
    {
        await PublishWorkflowAsync(
            "journey-full",
            WorkflowTestSchemas.WithDecision()).ConfigureAwait(false);
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(patientId).ConfigureAwait(false);

        Guid pipelineId = await StartPipelineIdAsync(
            "journey-full",
            subjectType: "encounter",
            subjectId: encounterId).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0)
            .ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 1)
            .ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 2)
            .ConfigureAwait(false);

        using JsonDocument response = await GetEncounterJourneyAsync(encounterId)
            .ConfigureAwait(false);
        JsonElement journey = response.RootElement
            .GetProperty("journeys")
            .EnumerateArray()
            .Single();
        Assert.Equal("completed", Str(journey, "status"));
        Assert.NotNull(GetOrNull(journey, "endedAt"));

        string[] actions = [.. journey.GetProperty("history").EnumerateArray()
            .Select(item => item.GetProperty("action").GetString()!)];
        Assert.Equal(
            [
                "pipeline.started",
                "pipeline.advanced",
                "pipeline.advanced",
                "pipeline.completed",
            ],
            actions);

        int[] sequences = [.. journey.GetProperty("history").EnumerateArray()
            .Select(item => item.GetProperty("sequence").GetInt32())];
        Assert.Equal([1, 2, 3, 4], sequences);
    }

    [Fact]
    public async Task Start_OnTerminalEncounter_Conflicts()
    {
        await PublishWorkflowAsync(
            "journey-enc-state",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid completed = await SeedEncounterAsync(patientId).ConfigureAwait(false);
        Guid canceled = await SeedEncounterAsync(patientId).ConfigureAwait(false);
        Guid errored = await SeedEncounterAsync(patientId).ConfigureAwait(false);

        _ = await TransitionEncounterAsync(completed, "complete")
            .ConfigureAwait(false);
        _ = await TransitionEncounterAsync(canceled, "cancel").ConfigureAwait(false);
        _ = await TransitionEncounterAsync(errored, "enter-in-error")
            .ConfigureAwait(false);

        foreach (Guid encounterId in new[] { completed, canceled, errored })
        {
            (HttpStatusCode status, JsonDocument _) = await StartPipelineRawAsync(
                "journey-enc-state",
                subjectType: "encounter",
                subjectId: encounterId).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.Conflict, status);
        }
    }

    [Fact]
    public async Task Start_OnSoftDeletedPatient_Conflicts()
    {
        await PublishWorkflowAsync(
            "journey-patient-state",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);

        using HttpResponseMessage deleted = await Client.PostAsync(
            new Uri($"/api/patients/{patientId:D}/soft-delete", UriKind.Relative),
            new StringContent(
                /*lang=json,strict*/ """{ "rowVersion": 0 }""",
                Encoding.UTF8,
                JsonApiMedia.ContentType)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        (HttpStatusCode status, JsonDocument _) = await StartPipelineRawAsync(
            "journey-patient-state",
            subjectType: "patient",
            subjectId: patientId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Journey_UnknownPatient_NotFound()
    {
        (HttpStatusCode status, JsonDocument _) = await GetPatientJourneyRawAsync(
            Guid.NewGuid()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Journey_UnknownEncounter_NotFound()
    {
        (HttpStatusCode status, JsonDocument _) = await GetEncounterJourneyRawAsync(
            Guid.NewGuid()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Journey_RequiresExactlyOneSubject_IsBadRequest()
    {
        using HttpResponseMessage neither = await Client.GetAsync(
            new Uri("/api/pipelines/journey", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);

        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(patientId).ConfigureAwait(false);
        using HttpResponseMessage both = await Client.GetAsync(
            new Uri(
                $"/api/pipelines/journey?patientId={patientId:D}"
                + $"&encounterId={encounterId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByPatientIdAndEncounterId()
    {
        await PublishWorkflowAsync(
            "journey-list",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(patientId).ConfigureAwait(false);

        _ = await StartPipelineIdAsync(
            "journey-list",
            subjectType: "patient",
            subjectId: patientId).ConfigureAwait(false);
        _ = await StartPipelineIdAsync(
            "journey-list",
            subjectType: "encounter",
            subjectId: encounterId).ConfigureAwait(false);

        using JsonDocument byPatient = await ListPipelinesAsync(
            $"patientId={patientId:D}").ConfigureAwait(false);
        Assert.Equal(2, EnumeratePipelines(byPatient).Length);

        using JsonDocument byEncounter = await ListPipelinesAsync(
            $"encounterId={encounterId:D}").ConfigureAwait(false);
        Assert.Single(EnumeratePipelines(byEncounter));
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private WorkflowTestApplicationFactory Factory { get; }

    private async Task<string> PublishWorkflowAsync(
        string code,
        string workflowSchemaJson)
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
        string draftId = await GetDraftIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument inReview = await PostVersionActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        using JsonDocument published = await PostVersionActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
        return JsonApiClient.RequireId(published);
    }

    private async Task PublishNextVersionWithSchemaAsync(
        string code,
        string workflowSchemaJson)
    {
        string definitionId = await FindDefinitionIdAsync(code).ConfigureAwait(false);
        using HttpResponseMessage created = await Client.PostAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string draftId = await GetDraftIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(draftId).ConfigureAwait(false);
        using HttpResponseMessage patched = await PatchDraftAsync(
            draftId,
            workflowSchemaJson,
            rowVersion).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        using var patchedBody = JsonDocument.Parse(
            await patched.Content.ReadAsStringAsync().ConfigureAwait(false));

        using JsonDocument inReview = await PostVersionActionAsync(
            draftId,
            "submit-review",
            JsonApiClient.AttrUInt(patchedBody, "rowVersion")).ConfigureAwait(false);
        _ = await PostVersionActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
    }

    private async Task<string> FindDefinitionIdAsync(string code)
    {
        using JsonDocument list = await Api.GetAsync(
            "/api/workflowDefinitions").ConfigureAwait(false);
        foreach (JsonElement item in list.RootElement.GetProperty("data").EnumerateArray())
        {
            if (string.Equals(
                item.GetProperty("attributes").GetProperty("code").GetString(),
                code,
                StringComparison.Ordinal))
            {
                return item.GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException($"Workflow '{code}' not found.");
    }

    private async Task<string> GetDraftIdAsync(string definitionId)
    {
        using JsonDocument definition = await Api.GetAsync(
            $"/api/workflowDefinitions/{definitionId}?include=versions")
            .ConfigureAwait(false);
        return definition.RootElement.GetProperty("included")
            .EnumerateArray()
            .First(item => string.Equals(
                item.GetProperty("attributes").GetProperty("status").GetString(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
            .GetProperty("id")
            .GetString()!;
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

    private async Task<HttpResponseMessage> PatchDraftAsync(
        string versionId,
        string workflowSchemaJson,
        uint rowVersion)
    {
        using StringContent content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "workflowVersions",
                id = versionId,
                attributes = new
                {
                    workflowSchemaJson,
                    rowVersion,
                },
            },
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/workflowVersions/{versionId}", UriKind.Relative))
        {
            Content = content,
        };
        return await Client.SendAsync(request).ConfigureAwait(false);
    }

    private async Task<Guid> StartPipelineIdAsync(
        string workflowCode,
        string subjectType,
        Guid subjectId)
    {
        using JsonDocument document = await StartPipelineAsync(
            workflowCode,
            subjectType,
            subjectId).ConfigureAwait(false);
        return Guid.Parse(
            document.RootElement.GetProperty("id").GetString()!,
            CultureInfo.InvariantCulture);
    }

    private async Task<JsonDocument> StartPipelineAsync(
        string workflowCode,
        string subjectType,
        Guid subjectId)
    {
        (HttpStatusCode status, JsonDocument document) = await StartPipelineRawAsync(
            workflowCode,
            subjectType,
            subjectId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> StartPipelineRawAsync(
        string workflowCode,
        string subjectType,
        Guid subjectId)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["workflowCode"] = workflowCode,
            ["subjectType"] = subjectType,
            ["subjectId"] = subjectId,
        };
        return await PostJsonAsync("/api/pipelines", payload).ConfigureAwait(false);
    }

    private async Task<JsonDocument> AdvancePipelineAsync(
        Guid pipelineId,
        uint rowVersion)
    {
        (HttpStatusCode status, JsonDocument document) = await PostJsonAsync(
            $"/api/pipelines/{pipelineId:D}/advance",
            new { rowVersion }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<JsonDocument> CompletePipelineAsync(
        Guid pipelineId,
        uint rowVersion)
    {
        (HttpStatusCode status, JsonDocument document) = await PostJsonAsync(
            $"/api/pipelines/{pipelineId:D}/complete",
            new { rowVersion }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<JsonDocument> TransitionEncounterAsync(
        Guid encounterId,
        string action)
    {
        (HttpStatusCode status, JsonDocument document) = await PostJsonAsync(
            $"/api/encounters/{encounterId:D}/{action}",
            new { rowVersion = 0 },
            JsonApiMedia.ContentType).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<JsonDocument> GetPatientJourneyAsync(Guid patientId)
    {
        (HttpStatusCode status, JsonDocument document) = await GetPatientJourneyRawAsync(
            patientId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> GetPatientJourneyRawAsync(
        Guid patientId)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri(
                $"/api/pipelines/journey?patientId={patientId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        return await ReadBodyAsync(response).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetEncounterJourneyAsync(Guid encounterId)
    {
        (HttpStatusCode status, JsonDocument document) = await GetEncounterJourneyRawAsync(
            encounterId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> GetEncounterJourneyRawAsync(
        Guid encounterId)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri(
                $"/api/pipelines/journey?encounterId={encounterId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        return await ReadBodyAsync(response).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ListPipelinesAsync(string query)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/pipelines?{query}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (_, JsonDocument body) = await ReadBodyAsync(response).ConfigureAwait(false);
        return body;
    }

    private async Task<Guid> SeedPatientAsync()
    {
        return await CreatePlainAsync(
            "/api/patients",
            new
            {
                mrn = $"MRN-{Guid.NewGuid():N}",
                nationalId = (string?)null,
                givenName = "Ada",
                familyName = "Lovelace",
                birthDate = "1990-01-01",
                sex = "female",
            }).ConfigureAwait(false);
    }

    private async Task<Guid> SeedEncounterAsync(Guid patientId)
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
        (_, JsonDocument document) = await ReadBodyAsync(response).ConfigureAwait(false);
        return Guid.Parse(
            document.RootElement.GetProperty("id").GetString()!,
            CultureInfo.InvariantCulture);
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> PostJsonAsync(
        string path,
        object? body,
        string? contentType = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                contentType ?? "application/json");
        }

        using HttpResponseMessage response = await Client.SendAsync(request)
            .ConfigureAwait(false);
        return await ReadBodyAsync(response).ConfigureAwait(false);
    }

    private static async Task<(HttpStatusCode Status, JsonDocument Body)> ReadBodyAsync(
        HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return (
            response.StatusCode,
            JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text));
    }

    private static string[] EnumeratePipelines(JsonDocument list)
    {
        return [.. list.RootElement.GetProperty("pipelines")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)];
    }

    private static string Str(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetString()!;
    }

    private static string Str(JsonElement element, string name)
    {
        return element.GetProperty(name).GetString()!;
    }

    private static JsonElement? GetOrNull(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    private static string NextVersionSchema()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin v2" },
                { "id": "collect", "type": "task", "name": "Collect v2" },
                { "id": "end", "type": "end", "name": "Done v2" }
              ],
              "edges": [
                { "from": "start", "to": "collect" },
                { "from": "collect", "to": "end" }
              ]
            }
            """;
    }
}

/// <summary>
/// Cross-tenant isolation for pipeline journeys: one hospital cannot query
/// the journey of another hospital's patient or encounter.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PipelineJourneyTenantIsolationTests : IDisposable
{
    public PipelineJourneyTenantIsolationTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        OtherClient = Factory.CreateClient();
        OtherClient.AcceptJsonApi();
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(CynaraTenantWebApplicationFactory.PrimaryCode);
        OtherClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            CynaraTenantWebApplicationFactory.OtherCode);

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
    public async Task CrossTenant_Journey_IsNotVisible()
    {
        await PublishWorkflowAsync("isolation-journey").ConfigureAwait(false);
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
            }).ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(patientId).ConfigureAwait(false);

        _ = await StartPipelineAsync(encounterId).ConfigureAwait(false);

        using HttpResponseMessage patientJourney = await OtherClient.GetAsync(
            new Uri(
                $"/api/pipelines/journey?patientId={patientId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, patientJourney.StatusCode);

        using HttpResponseMessage encounterJourney = await OtherClient.GetAsync(
            new Uri(
                $"/api/pipelines/journey?encounterId={encounterId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, encounterJourney.StatusCode);

        using HttpResponseMessage primaryJourney = await Client.GetAsync(
            new Uri(
                $"/api/pipelines/journey?encounterId={encounterId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, primaryJourney.StatusCode);
        using var primaryBody = JsonDocument.Parse(
            await primaryJourney.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Single(primaryBody.RootElement.GetProperty("journeys").EnumerateArray());
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    private JsonApiClient Api { get; }

    private async Task PublishWorkflowAsync(string code)
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code,
                name = code,
                initialWorkflowSchemaJson = WorkflowTestSchemas.Minimal(),
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

    private async Task<JsonDocument> StartPipelineAsync(Guid encounterId)
    {
        var payload = new
        {
            workflowCode = "isolation-journey",
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

    private async Task<Guid> SeedEncounterAsync(Guid patientId)
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
