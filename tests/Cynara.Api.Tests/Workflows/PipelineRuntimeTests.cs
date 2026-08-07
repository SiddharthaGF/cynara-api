using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Audit;
using Cynara.Domain.Failures;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// HTTP-level tests for the workflow pipeline runtime: starting pins the exact
/// published workflow version, advance evaluates transition guards/conditions
/// server-side and moves the cursor along the graph, lifecycle operations drive
/// the explicit state machine, and every transition appends to the immutable
/// progression history with audit events.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PipelineRuntimeTests : IDisposable
{
    public PipelineRuntimeTests(PostgreSqlDatabaseFixture database)
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
    public async Task Start_PinsLatestPublishedVersion_AndBeginsAtStartNode()
    {
        string publishedId = await PublishWorkflowAsync(
            "minimal-start",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        (Guid patientId, Guid encounterId) = await SeedEncounterAsync()
            .ConfigureAwait(false);

        using JsonDocument started = await StartPipelineAsync(
            "minimal-start",
            workflowVersion: null,
            subjectType: "encounter",
            subjectId: encounterId).ConfigureAwait(false);

        Assert.Equal("running", Str(started, "status"));
        Assert.Equal("start", Str(started, "currentNodeId"));
        Assert.Equal("minimal-start", Str(started, "workflowCode"));
        Assert.Equal("1.0.0", Str(started, "workflowVersion"));
        Assert.Equal("1.0.0", Str(started, "workflowSchemaVersion"));
        Assert.Equal("encounter", Str(started, "subjectType"));
        Assert.Equal(encounterId.ToString("D", CultureInfo.InvariantCulture), Str(started, "subjectId"));
        Assert.Equal(patientId.ToString("D", CultureInfo.InvariantCulture), Str(started, "patientId"));
        Assert.Equal(encounterId.ToString("D", CultureInfo.InvariantCulture), Str(started, "encounterId"));
        Assert.Equal(0u, UInt(started, "rowVersion"));
        Assert.Equal(publishedId, Str(started, "workflowVersionId"));
        Assert.Null(GetOrNull(started, "endedAt"));
    }

    [Fact]
    public async Task Start_WithExplicitVersion_PinsRequestedVersion()
    {
        await PublishWorkflowAsync(
            "pinned-start",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        await PublishNextVersionAsync("pinned-start").ConfigureAwait(false);
        Guid patientId = await SeedPatientAsync().ConfigureAwait(false);

        using JsonDocument older = await StartPipelineAsync(
            "pinned-start",
            "1.0.0",
            "patient",
            patientId).ConfigureAwait(false);
        Assert.Equal("1.0.0", Str(older, "workflowVersion"));

        using JsonDocument newer = await StartPipelineAsync(
            "pinned-start",
            "1.0.1",
            "patient",
            patientId).ConfigureAwait(false);
        Assert.Equal("1.0.1", Str(newer, "workflowVersion"));
    }

    [Fact]
    public async Task Start_OnUnknownSubject_NotFound()
    {
        await PublishWorkflowAsync(
            "unknown-subject",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        (HttpStatusCode status, JsonDocument _) = await StartPipelineRawAsync(
            "unknown-subject",
            workflowVersion: null,
            subjectType: "encounter",
            subjectId: Guid.NewGuid()).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Start_WithoutPublishedVersion_NotFound()
    {
        _ = await CreateDefinitionAsync(
            "unpublished-start",
            "Unpublished workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        (HttpStatusCode status, JsonDocument _) = await StartPipelineRawAsync(
            "unpublished-start",
            workflowVersion: null,
            subjectType: "encounter").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Start_WithUnknownVersion_NotFound()
    {
        await PublishWorkflowAsync(
            "unknown-version",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        (HttpStatusCode status, JsonDocument _) = await StartPipelineRawAsync(
            "unknown-version",
            workflowVersion: "9.9.9",
            subjectType: "encounter").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Start_OnRetiredVersion_NotFound()
    {
        string definitionId = await CreateDefinitionAsync(
            "retired-start",
            "Retired workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await GetDraftIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument inReview = await PostVersionActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        _ = await PostVersionActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
        _ = await PostVersionActionAsync(
            draftId,
            "retire",
            rowVersion: null).ConfigureAwait(false);

        (HttpStatusCode status, JsonDocument _) = await StartPipelineRawAsync(
            "retired-start",
            workflowVersion: "1.0.0",
            subjectType: "encounter").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Advance_TaskToEnd_CompletesPipeline()
    {
        _ = await PublishWorkflowAsync(
            "complete-flow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "complete-flow",
            subjectType: "encounter").ConfigureAwait(false);

        using JsonDocument advanced = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", Str(advanced, "status"));
        Assert.Equal("end", Str(advanced, "currentNodeId"));
        Assert.NotNull(GetOrNull(advanced, "endedAt"));
        Assert.Equal(1u, UInt(advanced, "rowVersion"));
    }

    [Fact]
    public async Task Advance_OnCompletedPipeline_Conflicts()
    {
        _ = await PublishWorkflowAsync(
            "already-complete",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "already-complete",
            subjectType: "encounter").ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0).ConfigureAwait(false);

        (HttpStatusCode status, JsonDocument _) = await AdvancePipelineRawAsync(
            pipelineId,
            rowVersion: 1).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Advance_Decision_EvaluatesConditionsServerSide()
    {
        _ = await PublishWorkflowAsync(
            "triage-flow",
            WorkflowTestSchemas.WithDecision()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "triage-flow",
            subjectType: "encounter").ConfigureAwait(false);
        var lowScore = Inputs("triage-score", 3);

        using JsonDocument first = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 0,
            lowScore).ConfigureAwait(false);
        Assert.Equal("triage", Str(first, "currentNodeId"));

        using JsonDocument second = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 1,
            lowScore).ConfigureAwait(false);
        Assert.Equal("low-task", Str(second, "currentNodeId"));

        using JsonDocument third = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 2).ConfigureAwait(false);
        Assert.Equal("completed", Str(third, "status"));
        Assert.Equal("end", Str(third, "currentNodeId"));
    }

    [Fact]
    public async Task Advance_Decision_FallsBackToDefaultEdge()
    {
        _ = await PublishWorkflowAsync(
            "triage-default",
            WorkflowTestSchemas.WithDecision()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "triage-default",
            subjectType: "encounter").ConfigureAwait(false);
        var highScore = Inputs("triage-score", 9);

        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0, highScore)
            .ConfigureAwait(false);

        using JsonDocument branch = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 1,
            highScore).ConfigureAwait(false);
        Assert.Equal("high-task", Str(branch, "currentNodeId"));
    }

    [Fact]
    public async Task Advance_Decision_NoMatchNoDefault_Conflicts()
    {
        _ = await PublishWorkflowAsync(
            "strict-decision",
            StrictDecisionFlow()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "strict-decision",
            subjectType: "encounter").ConfigureAwait(false);

        _ = await AdvancePipelineAsync(
            pipelineId,
            rowVersion: 0,
            Inputs("level", "medium")).ConfigureAwait(false);

        (HttpStatusCode status, JsonDocument _) = await AdvancePipelineRawAsync(
            pipelineId,
            rowVersion: 1,
            Inputs("level", "medium")).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Advance_StaleRowVersion_Conflicts()
    {
        _ = await PublishWorkflowAsync(
            "stale-pipeline",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "stale-pipeline",
            subjectType: "encounter").ConfigureAwait(false);

        (HttpStatusCode status, JsonDocument _) = await AdvancePipelineRawAsync(
            pipelineId,
            rowVersion: 42).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task CompleteCancelEnterInError_DriveTerminalStates()
    {
        _ = await PublishWorkflowAsync(
            "lifecycle-flow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        Guid completed = await StartPipelineIdAsync(
            "lifecycle-flow",
            subjectType: "encounter").ConfigureAwait(false);
        using JsonDocument complete = await CompletePipelineAsync(
            completed,
            rowVersion: 0,
            reason: "Intake finished early").ConfigureAwait(false);
        Assert.Equal("completed", Str(complete, "status"));
        Assert.NotNull(GetOrNull(complete, "endedAt"));

        Guid canceled = await StartPipelineIdAsync(
            "lifecycle-flow",
            subjectType: "encounter").ConfigureAwait(false);
        using JsonDocument cancel = await CancelPipelineAsync(
            canceled,
            rowVersion: 0,
            reason: "Patient declined").ConfigureAwait(false);
        Assert.Equal("canceled", Str(cancel, "status"));

        Guid errored = await StartPipelineIdAsync(
            "lifecycle-flow",
            subjectType: "encounter").ConfigureAwait(false);
        using JsonDocument enterInError = await EnterInErrorPipelineAsync(
            errored,
            rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("enteredInError", Str(enterInError, "status"));
    }

    [Fact]
    public async Task Transition_AfterTerminalState_Conflicts()
    {
        _ = await PublishWorkflowAsync(
            "terminal-conflict",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "terminal-conflict",
            subjectType: "encounter").ConfigureAwait(false);
        using JsonDocument canceled = await CancelPipelineAsync(
            pipelineId,
            rowVersion: 0,
            reason: "Stopped").ConfigureAwait(false);
        Assert.Equal("canceled", Str(canceled, "status"));

        (HttpStatusCode completeStatus, JsonDocument _) = await CompletePipelineRawAsync(
            pipelineId,
            rowVersion: 1).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, completeStatus);

        (HttpStatusCode advanceStatus, JsonDocument _) = await AdvancePipelineRawAsync(
            pipelineId,
            rowVersion: 1).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, advanceStatus);
    }

    [Fact]
    public async Task List_FiltersByStatusAndSubject()
    {
        _ = await PublishWorkflowAsync(
            "list-flow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        (_, Guid subjectId) = await SeedEncounterAsync().ConfigureAwait(false);
        _ = await StartPipelineIdAsync(
            "list-flow",
            subjectType: "encounter").ConfigureAwait(false);
        Guid matching = await StartPipelineIdAsync(
            "list-flow",
            subjectType: "encounter",
            subjectId).ConfigureAwait(false);

        using JsonDocument bySubject = await ListPipelinesAsync(
            $"subjectId={subjectId:D}").ConfigureAwait(false);
        string[] subjectPipelines = EnumeratePipelines(bySubject);
        Assert.Single(subjectPipelines);
        Assert.Equal(matching.ToString("D", CultureInfo.InvariantCulture), subjectPipelines[0]);

        using JsonDocument byStatus = await ListPipelinesAsync("status=running")
            .ConfigureAwait(false);
        Assert.Equal(2, EnumeratePipelines(byStatus).Length);

        using JsonDocument bySubjectAndStatus = await ListPipelinesAsync(
            $"subjectId={subjectId:D}&status=running").ConfigureAwait(false);
        Assert.Single(EnumeratePipelines(bySubjectAndStatus));
    }

    [Fact]
    public async Task History_IsAppendOnlyAndOrdered()
    {
        _ = await PublishWorkflowAsync(
            "history-flow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "history-flow",
            subjectType: "encounter").ConfigureAwait(false);
        _ = await CompletePipelineAsync(
            pipelineId,
            rowVersion: 0,
            reason: "Done").ConfigureAwait(false);

        using JsonDocument history = await GetHistoryAsync(pipelineId)
            .ConfigureAwait(false);
        JsonElement historyArray = history.RootElement.GetProperty("history");
        string[] actions = [.. historyArray.EnumerateArray()
            .Select(item => item.GetProperty("action").GetString()!)];
        Assert.Equal(["pipeline.started", "pipeline.completed"], actions);

        int[] sequences = [.. historyArray.EnumerateArray()
            .Select(item => item.GetProperty("sequence").GetInt32())];
        Assert.Equal([1, 2], sequences);

        Assert.Equal(
            pipelineId.ToString("D", CultureInfo.InvariantCulture),
            history.RootElement.GetProperty("pipelineId").GetString());
    }

    [Fact]
    public async Task Audit_RecordsPipelineEvents()
    {
        _ = await PublishWorkflowAsync(
            "audit-flow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "audit-flow",
            subjectType: "encounter").ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0).ConfigureAwait(false);

        await AssertAuditEventsRecordedAsync(
            pipelineId,
            "pipeline.started",
            "pipeline.completed").ConfigureAwait(false);
    }

    [Fact]
    public async Task Get_UnknownPipeline_NotFound()
    {
        (HttpStatusCode status, JsonDocument _) = await GetPipelineAsync(Guid.NewGuid())
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private WorkflowTestApplicationFactory Factory { get; }

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

    private async Task<string> PublishWorkflowAsync(
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
        return JsonApiClient.RequireId(published);
    }

    private async Task PublishNextVersionAsync(string code)
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
        using JsonDocument inReview = await PostVersionActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
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

    private async Task<JsonDocument> AdvancePipelineAsync(
        Guid pipelineId,
        uint rowVersion,
        Dictionary<string, object>? inputValues = null)
    {
        (HttpStatusCode status, JsonDocument document) = await AdvancePipelineRawAsync(
            pipelineId,
            rowVersion,
            inputValues).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> AdvancePipelineRawAsync(
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

        return await PostJsonAsync(
            $"/api/pipelines/{pipelineId:D}/advance",
            payload).ConfigureAwait(false);
    }

    private async Task<JsonDocument> CompletePipelineAsync(
        Guid pipelineId,
        uint rowVersion,
        string? reason = null)
    {
        (HttpStatusCode status, JsonDocument document) = await CompletePipelineRawAsync(
            pipelineId,
            rowVersion,
            reason).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> CompletePipelineRawAsync(
        Guid pipelineId,
        uint rowVersion,
        string? reason = null)
    {
        return await LifecycleRawAsync(
            pipelineId,
            "complete",
            rowVersion,
            reason).ConfigureAwait(false);
    }

    private async Task<JsonDocument> CancelPipelineAsync(
        Guid pipelineId,
        uint rowVersion,
        string? reason = null)
    {
        (HttpStatusCode status, JsonDocument document) = await LifecycleRawAsync(
            pipelineId,
            "cancel",
            rowVersion,
            reason).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<JsonDocument> EnterInErrorPipelineAsync(
        Guid pipelineId,
        uint rowVersion)
    {
        (HttpStatusCode status, JsonDocument document) = await LifecycleRawAsync(
            pipelineId,
            "enter-in-error",
            rowVersion,
            reason: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> LifecycleRawAsync(
        Guid pipelineId,
        string action,
        uint rowVersion,
        string? reason)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rowVersion"] = rowVersion,
        };
        if (reason is not null)
        {
            payload["reason"] = reason;
        }

        return await PostJsonAsync(
            $"/api/pipelines/{pipelineId:D}/{action}",
            payload).ConfigureAwait(false);
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> GetPipelineAsync(
        Guid pipelineId)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/pipelines/{pipelineId:D}", UriKind.Relative))
            .ConfigureAwait(false);
        return await ReadBodyAsync(response).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetHistoryAsync(Guid pipelineId)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri($"/api/pipelines/{pipelineId:D}/history", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (_, JsonDocument body) = await ReadBodyAsync(response).ConfigureAwait(false);
        return body;
    }

    private async Task<JsonDocument> ListPipelinesAsync(string? query = null)
    {
        string path = query is null ? "/api/pipelines" : $"/api/pipelines?{query}";
        using HttpResponseMessage response = await Client.GetAsync(
            new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (_, JsonDocument body) = await ReadBodyAsync(response).ConfigureAwait(false);
        return body;
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

    private async Task<(HttpStatusCode Status, JsonDocument Body)> ReadBodyAsync(
        HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            CynaraDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<CynaraDbContext>();
            FailureLog failure = await dbContext.FailureLogs
                .OrderByDescending(item => item.OccurredAt)
                .FirstAsync()
                .ConfigureAwait(false);
            Assert.Fail(
                $"Server error: {text}{Environment.NewLine}"
                + $"{failure.ExceptionType}: {failure.Message}{Environment.NewLine}"
                + $"{failure.StackTrace}");
        }

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

    private static Dictionary<string, object> Inputs(string name, object value)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [name] = value,
        };
    }

    private static string Str(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetString()!;
    }

    private static uint UInt(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetUInt32();
    }

    private static JsonElement? GetOrNull(JsonDocument document, string name)
    {
        return document.RootElement.TryGetProperty(name, out JsonElement value)
            && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    private async Task AssertAuditEventsRecordedAsync(
        Guid resourceId,
        params string[] actions)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        List<AuditEvent> events = [.. (await dbContext.AuditEvents
            .Where(item => item.ResourceId == resourceId)
            .ToListAsync()
            .ConfigureAwait(false))
            .OrderBy(item => item.OccurredAt)];

        foreach (string action in actions)
        {
            Assert.Contains(
                events,
                item => string.Equals(item.Action, action, StringComparison.Ordinal));
        }
    }

    private static string StrictDecisionFlow()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "inputs": ["level"],
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "triage", "type": "decision", "name": "Triage" },
                { "id": "low-task", "type": "task", "name": "Low" },
                { "id": "high-task", "type": "task", "name": "High" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "triage" },
                {
                  "from": "triage",
                  "to": "low-task",
                  "condition": {
                    "op": "eq",
                    "args": [ { "ref": "level" }, { "lit": "low" } ]
                  }
                },
                {
                  "from": "triage",
                  "to": "high-task",
                  "condition": {
                    "op": "eq",
                    "args": [ { "ref": "level" }, { "lit": "high" } ]
                  }
                },
                { "from": "low-task", "to": "end" },
                { "from": "high-task", "to": "end" }
              ]
            }
            """;
    }
}
