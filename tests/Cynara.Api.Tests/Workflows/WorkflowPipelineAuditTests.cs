using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// CYN-68 audit coverage for workflow configuration and pipeline activity:
/// mutation events carry queryable patient/encounter/workflow subject
/// columns, sensitive workflow and journey reads emit read events, and the
/// append-only audit surface stays read-only.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowPipelineAuditTests : IDisposable
{
    private const string Actor = "auditor-1";

    public WorkflowPipelineAuditTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new WorkflowTestApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", Actor);
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
    public async Task WorkflowDefinitionEvents_StampWorkflowDefinitionId()
    {
        (Guid definitionId, _) = await PublishWorkflowAsync(
            "audit-def-flow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        using JsonDocument document = await QueryAuditEventsAsync(
            $"filter=equals(workflowDefinitionId,'{definitionId:D}')")
            .ConfigureAwait(false);
        JsonElement events = document.RootElement.GetProperty("data");

        string[] actions = [.. events.EnumerateArray()
            .Select(item => item.GetProperty("attributes")
                .GetProperty("action").GetString()!)];
        Assert.Contains("workflow.created", actions);
        Assert.Contains("workflow.draft.submitted-for-review", actions);
        Assert.Contains("workflow.version.published", actions);

        Assert.All(
            events.EnumerateArray(),
            item => Assert.Equal(
                definitionId.ToString("D", CultureInfo.InvariantCulture),
                item.GetProperty("attributes")
                    .GetProperty("workflowDefinitionId").GetString()));
    }

    [Fact]
    public async Task PipelineAndTaskEvents_StampPatientEncounterAndWorkflow()
    {
        (Guid definitionId, _) = await PublishWorkflowAsync(
            "audit-pipe-flow",
            WorkflowTestSchemas.WithDecision()).ConfigureAwait(false);
        (Guid patientId, Guid encounterId) = await SeedEncounterAsync()
            .ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "audit-pipe-flow",
            subjectType: "encounter",
            encounterId).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 0,
            Inputs("triage-score", 3)).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 1,
            Inputs("triage-score", 3)).ConfigureAwait(false);

        using JsonDocument byPatient = await QueryAuditEventsAsync(
            $"filter=equals(patientId,'{patientId:D}')").ConfigureAwait(false);
        JsonElement patientEvents = byPatient.RootElement.GetProperty("data");
        string[] patientActions = [.. patientEvents.EnumerateArray()
            .Select(item => item.GetProperty("attributes")
                .GetProperty("action").GetString()!)];
        Assert.Contains("pipeline.started", patientActions);
        Assert.Contains("pipeline.advanced", patientActions);
        Assert.Contains("task.generated", patientActions);

        Assert.All(
            patientEvents.EnumerateArray(),
            item =>
            {
                JsonElement attributes = item.GetProperty("attributes");
                Assert.Equal(
                    patientId.ToString("D", CultureInfo.InvariantCulture),
                    attributes.GetProperty("patientId").GetString());
                Assert.Equal(
                    encounterId.ToString("D", CultureInfo.InvariantCulture),
                    attributes.GetProperty("encounterId").GetString());
                Assert.Equal(
                    definitionId.ToString("D", CultureInfo.InvariantCulture),
                    attributes.GetProperty("workflowDefinitionId").GetString());
            });

        using JsonDocument byEncounter = await QueryAuditEventsAsync(
            $"filter=equals(encounterId,'{encounterId:D}')").ConfigureAwait(false);
        JsonElement encounterEvents = byEncounter.RootElement.GetProperty("data");
        Assert.Contains(
            encounterEvents.EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "pipeline.started",
                StringComparison.Ordinal));

        using JsonDocument byWorkflow = await QueryAuditEventsAsync(
            $"filter=equals(workflowDefinitionId,'{definitionId:D}')")
            .ConfigureAwait(false);
        JsonElement workflowEvents = byWorkflow.RootElement.GetProperty("data");
        string[] workflowActions = [.. workflowEvents.EnumerateArray()
            .Select(item => item.GetProperty("attributes")
                .GetProperty("action").GetString()!)];
        Assert.Contains("pipeline.started", workflowActions);
        Assert.Contains("task.generated", workflowActions);
    }

    /// <summary>
    /// The publish helper reads the definition while resolving the draft, so a
    /// workflow.read event exists for every successful definition read.
    /// </summary>
    [Fact]
    public async Task WorkflowDefinitionRead_EmitsSensitiveReadAuditEvent()
    {
        (Guid definitionId, _) = await PublishWorkflowAsync(
            "audit-read-def",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        using JsonDocument response = await Api.GetAsync(
            $"/api/workflowDefinitions/{definitionId:D}").ConfigureAwait(false);
        Assert.Equal(
            definitionId.ToString("D", CultureInfo.InvariantCulture),
            JsonApiClient.RequireId(response));

        using JsonDocument audit = await QueryAuditEventsAsync(
            "filter=equals(action,'workflow.read')").ConfigureAwait(false);
        JsonElement[] readEvents = [.. audit.RootElement.GetProperty("data")
            .EnumerateArray()
            .Where(item => string.Equals(
                item.GetProperty("attributes").GetProperty("resourceType").GetString(),
                "workflow-definition",
                StringComparison.Ordinal))];

        Assert.NotEmpty(readEvents);
        JsonElement attributes = readEvents[0].GetProperty("attributes");
        Assert.Equal(
            definitionId.ToString("D", CultureInfo.InvariantCulture),
            attributes.GetProperty("resourceId").GetString());
        Assert.Equal(Actor, attributes.GetProperty("actorId").GetString());
        Assert.Contains(
            "\"requestPath\":\"/api/workflowDefinitions/",
            attributes.GetProperty("metadataJson").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The publish helper reads the version while resolving the draft, so a
    /// workflow.version.read event exists for every version.
    /// </summary>
    [Fact]
    public async Task WorkflowVersionRead_EmitsSensitiveReadAuditEvent()
    {
        (_, Guid versionId) = await PublishWorkflowAsync(
            "audit-read-version",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        using JsonDocument response = await Api.GetAsync(
            $"/api/workflowVersions/{versionId:D}").ConfigureAwait(false);
        Assert.Equal(
            versionId.ToString("D", CultureInfo.InvariantCulture),
            JsonApiClient.RequireId(response));

        using JsonDocument audit = await QueryAuditEventsAsync(
            "filter=equals(action,'workflow.version.read')").ConfigureAwait(false);
        JsonElement[] readEvents = [.. audit.RootElement.GetProperty("data")
            .EnumerateArray()
            .Where(item => string.Equals(
                item.GetProperty("attributes").GetProperty("resourceType").GetString(),
                "workflow-version",
                StringComparison.Ordinal))];

        Assert.NotEmpty(readEvents);
        JsonElement attributes = readEvents[0].GetProperty("attributes");
        Assert.Equal(
            versionId.ToString("D", CultureInfo.InvariantCulture),
            attributes.GetProperty("resourceId").GetString());
        Assert.Equal(Actor, attributes.GetProperty("actorId").GetString());
    }

    [Fact]
    public async Task JourneyReads_EmitsSensitiveReadAuditEvents()
    {
        _ = await PublishWorkflowAsync(
            "audit-journey-flow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        (Guid patientId, Guid encounterId) = await SeedEncounterAsync()
            .ConfigureAwait(false);
        _ = await StartPipelineIdAsync(
            "audit-journey-flow",
            subjectType: "encounter",
            encounterId).ConfigureAwait(false);

        using HttpResponseMessage patientJourney = await Client.GetAsync(
            new Uri(
                $"/api/pipelines/journey?patientId={patientId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, patientJourney.StatusCode);

        using HttpResponseMessage encounterJourney = await Client.GetAsync(
            new Uri(
                $"/api/pipelines/journey?encounterId={encounterId:D}",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, encounterJourney.StatusCode);

        using JsonDocument audit = await QueryAuditEventsAsync(
            "filter=equals(action,'pipeline.journey.read')").ConfigureAwait(false);
        JsonElement events = audit.RootElement.GetProperty("data");
        Assert.Equal(2, events.GetArrayLength());
        Assert.Contains(
            events.EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("resourceType").GetString(),
                "patient",
                StringComparison.Ordinal));
        Assert.Contains(
            events.EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("resourceType").GetString(),
                "encounter",
                StringComparison.Ordinal));
        Assert.All(
            events.EnumerateArray(),
            item => Assert.Equal(
                Actor,
                item.GetProperty("attributes").GetProperty("actorId").GetString()));
    }

    /// <summary>
    /// JADNC rejects the mutation before the read-only service runs (422) or
    /// the service raises InvalidStateException (409); either way the
    /// append-only audit surface never accepts writes.
    /// </summary>
    [Fact]
    public async Task AuditEvents_AreReadOnly()
    {
        var payload = new
        {
            data = new
            {
                type = "auditEvents",
                attributes = new
                {
                    resourceType = "test",
                    resourceId = Guid.NewGuid(),
                    action = "test.action",
                },
            },
        };

        using StringContent content = JsonApiClient.CreateJsonApiContent(payload);
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri("/api/auditEvents", UriKind.Relative), content)
            .ConfigureAwait(false);

        Assert.False(response.IsSuccessStatusCode);
    }

    private async Task<JsonDocument> QueryAuditEventsAsync(string filterQuery)
    {
        string path = $"/api/auditEvents?{filterQuery}";
        return await Api.GetAsync(path).ConfigureAwait(false);
    }

    private async Task<(Guid DefinitionId, Guid VersionId)> PublishWorkflowAsync(
        string code,
        string workflowSchemaJson)
    {
        string definitionId = await CreateDefinitionAsync(
            code,
            code,
            workflowSchemaJson).ConfigureAwait(false);
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
        return (
            Guid.Parse(definitionId, CultureInfo.InvariantCulture),
            Guid.Parse(
                JsonApiClient.RequireId(published),
                CultureInfo.InvariantCulture));
    }

    private async Task<string> CreateDefinitionAsync(
        string code,
        string name,
        string workflowSchemaJson)
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code,
                name,
                initialWorkflowSchemaJson = workflowSchemaJson,
            }).ConfigureAwait(false);
        return JsonApiClient.RequireId(created);
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
                StringComparison.Ordinal))
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

    private async Task<Guid> StartPipelineIdAsync(
        string workflowCode,
        string subjectType,
        Guid? subjectId = null)
    {
        using JsonDocument document = await StartPipelineAsync(
            workflowCode,
            workflowVersion: null,
            subjectType,
            subjectId).ConfigureAwait(false);
        return Guid.Parse(
            document.RootElement.GetProperty("id").GetString()!,
            CultureInfo.InvariantCulture);
    }

    private async Task<JsonDocument> StartPipelineAsync(
        string workflowCode,
        string? workflowVersion,
        string subjectType,
        Guid? subjectId = null)
    {
        (HttpStatusCode status, JsonDocument document) = await StartPipelineRawAsync(
            workflowCode,
            workflowVersion,
            subjectType,
            subjectId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> StartPipelineRawAsync(
        string workflowCode,
        string? workflowVersion,
        string subjectType,
        Guid? subjectId = null)
    {
        Guid resolvedSubjectId = subjectId ?? await ResolveSubjectIdAsync(subjectType)
            .ConfigureAwait(false);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["workflowCode"] = workflowCode,
            ["subjectType"] = subjectType,
            ["subjectId"] = resolvedSubjectId,
        };
        if (workflowVersion is not null)
        {
            payload["workflowVersion"] = workflowVersion;
        }

        return await PostJsonAsync("/api/pipelines", payload).ConfigureAwait(false);
    }

    private async Task<Guid> ResolveSubjectIdAsync(string subjectType)
    {
        if (string.Equals(
            subjectType,
            "encounter",
            StringComparison.OrdinalIgnoreCase))
        {
            (_, Guid encounterId) = await SeedEncounterAsync().ConfigureAwait(false);
            return encounterId;
        }

        return await SeedPatientAsync().ConfigureAwait(false);
    }

    private async Task<JsonDocument> AdvancePipelineAsync(
        Guid pipelineId,
        uint rowVersion,
        Dictionary<string, object>? inputValues = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rowVersion"] = rowVersion,
        };
        if (inputValues is not null)
        {
            payload["inputValues"] = inputValues;
        }

        (HttpStatusCode status, JsonDocument document) = await PostJsonAsync(
            $"/api/pipelines/{pipelineId:D}/advance",
            payload).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
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
                bloodType = "o+",
            }).ConfigureAwait(false);
    }

    private async Task<(Guid PatientId, Guid EncounterId)> SeedEncounterAsync()
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
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);
        Guid encounterId = await CreatePlainAsync(
            "/api/encounters",
            new
            {
                patientId,
                facilityId,
                clinicalAreaId,
                type = "ambulatory",
                responsibleProfessionalId = "dr-who",
            }).ConfigureAwait(false);
        return (patientId, encounterId);
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
        object? body)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
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

    private static Dictionary<string, object> Inputs(string name, object value)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [name] = value,
        };
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private WorkflowTestApplicationFactory Factory { get; }
}
