using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Api.Tests.Workflows;

namespace Cynara.Api.Tests.Tasks;

/// <summary>
/// HTTP-level tests for the clinical task runtime: tasks are generated from
/// the pinned published workflow definition when a pipeline enters a task
/// node, drive an explicit claim/complete/cancel lifecycle with optimistic
/// concurrency, are closed when the referenced clinical document completes,
/// and are canceled when the generating pipeline terminates.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class TaskRuntimeTests : IDisposable
{
    private const string PrimaryHospitalCode =
        CynaraTenantWebApplicationFactory.PrimaryCode;

    private const string SecondaryHospitalCode =
        CynaraTenantWebApplicationFactory.OtherCode;

    private const string Actor = "clinician";

    private readonly CynaraTenantWebApplicationFactory factory;
    private readonly HttpClient client;
    private readonly JsonApiClient api;
    private readonly ClinicalRecordWorkflow clinical;

    public TaskRuntimeTests(PostgreSqlDatabaseFixture database)
    {
        factory = new CynaraTenantWebApplicationFactory(database.Settings);
        client = factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        client.DefaultRequestHeaders.Add("X-Actor-Id", Actor);
        factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();

        api = new JsonApiClient(client);
        clinical = new ClinicalRecordWorkflow(api, client, factory);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Task_GeneratedOnEnteringTaskNode_MatchesPublishedDefinition()
    {
        _ = await PublishWorkflowAsync(
            "task-snapshot",
            WorkflowTestSchemas.WithTaskNode()).ConfigureAwait(false);
        (_, Guid encounterId) = await SeedWorkspaceEncounterAsync("snapshot")
            .ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "task-snapshot",
            subjectType: "encounter",
            encounterId).ConfigureAwait(false);

        DateTimeOffset beforeAdvance = DateTimeOffset.UtcNow;
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0)
            .ConfigureAwait(false);

        using JsonDocument list = await ListTasksAsync(
            $"pipelineId={pipelineId:D}").ConfigureAwait(false);
        JsonElement task = Assert.Single(list.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal("admit-task", Str(task, "nodeId"));
        Assert.Equal("Admission assessment", Str(task, "name"));
        Assert.Equal("Complete the admission assessment for this patient.", Str(task, "description"));
        Assert.Equal("open", Str(task, "status"));
        Assert.Equal("triage-service", Str(task, "assignedActor"));
        Assert.Equal("nurse", Str(task, "assignedRole"));
        Assert.Equal("nursing", Str(task, "assignedDiscipline"));
        Assert.Equal("admission-assessment", Str(task, "formCode"));
        Assert.Equal("1.0.0", Str(task, "formVersion"));
        Assert.Equal(encounterId.ToString("D", CultureInfo.InvariantCulture), Str(task, "encounterId"));

        var dueAt = DateTimeOffset.Parse(
            Str(task, "dueAt"), CultureInfo.InvariantCulture);
        double days = (dueAt - beforeAdvance).TotalDays;
        Assert.InRange(days, 3.0, 3.1);
    }

    [Fact]
    public async Task NoTask_ForStartDecisionEndNodes()
    {
        _ = await PublishWorkflowAsync(
            "task-none-minimal",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        (_, Guid minimalEncounter) = await SeedWorkspaceEncounterAsync("none-min")
            .ConfigureAwait(false);
        Guid minimalPipeline = await StartPipelineIdAsync(
            "task-none-minimal",
            subjectType: "encounter",
            minimalEncounter).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(minimalPipeline, rowVersion: 0)
            .ConfigureAwait(false);
        using (JsonDocument endList = await ListTasksAsync(
            $"pipelineId={minimalPipeline:D}").ConfigureAwait(false))
        {
            Assert.Empty(endList.RootElement.GetProperty("tasks").EnumerateArray());
        }

        _ = await PublishWorkflowAsync(
            "task-none-decision",
            WorkflowTestSchemas.WithDecision()).ConfigureAwait(false);
        (_, Guid decisionEncounter) = await SeedWorkspaceEncounterAsync("none-dec")
            .ConfigureAwait(false);
        Guid decisionPipeline = await StartPipelineIdAsync(
            "task-none-decision",
            subjectType: "encounter",
            decisionEncounter).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(
            decisionPipeline,
            rowVersion: 0,
            Inputs("triage-score", 3)).ConfigureAwait(false);
        using JsonDocument atDecision = await ListTasksAsync(
            $"pipelineId={decisionPipeline:D}").ConfigureAwait(false);
        Assert.Empty(atDecision.RootElement.GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task ClaimCompleteCancel_EnforceLifecycleAndConcurrency()
    {
        _ = await PublishWorkflowAsync(
            "task-lifecycle",
            WorkflowTestSchemas.WithTaskNode()).ConfigureAwait(false);
        Guid firstPatient = await SeedPatientAsync("lifecycle-a").ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "task-lifecycle",
            subjectType: "patient",
            firstPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0)
            .ConfigureAwait(false);

        Guid taskId = await GetSingleTaskIdAsync(pipelineId).ConfigureAwait(false);

        using (JsonDocument open = await GetTaskAsync(taskId).ConfigureAwait(false))
        {
            Assert.Equal("open", Str(open, "status"));
            Assert.Equal(0u, UInt(open, "rowVersion"));
        }

        using JsonDocument claimed = await ClaimTaskAsync(taskId, rowVersion: 0)
            .ConfigureAwait(false);
        Assert.Equal("claimed", Str(claimed, "status"));
        Assert.Equal(Actor, Str(claimed, "claimedBy"));
        Assert.Equal(1u, UInt(claimed, "rowVersion"));

        using JsonDocument completed = await CompleteTaskAsync(
            taskId, rowVersion: 1, reason: "Assessment done").ConfigureAwait(false);
        Assert.Equal("completed", Str(completed, "status"));
        Assert.Equal(Actor, Str(completed, "completedBy"));

        (HttpStatusCode completeAgainStatus, JsonDocument _) =
            await CompleteTaskRawAsync(taskId, rowVersion: 2).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, completeAgainStatus);

        (HttpStatusCode cancelAfterComplete, JsonDocument _) =
            await CancelTaskRawAsync(taskId, rowVersion: 2).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, cancelAfterComplete);

        Guid secondPatient = await SeedPatientAsync("lifecycle-b").ConfigureAwait(false);
        Guid secondPipeline = await StartPipelineIdAsync(
            "task-lifecycle",
            subjectType: "patient",
            secondPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(secondPipeline, rowVersion: 0)
            .ConfigureAwait(false);
        Guid secondTask = await GetSingleTaskIdAsync(secondPipeline).ConfigureAwait(false);
        (HttpStatusCode staleStatus, JsonDocument _) = await ClaimTaskRawAsync(
            secondTask, rowVersion: 42).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, staleStatus);

        using JsonDocument canceled = await CancelTaskAsync(
            secondTask, rowVersion: 0, reason: "No longer needed").ConfigureAwait(false);
        Assert.Equal("canceled", Str(canceled, "status"));
        Assert.Equal(Actor, Str(canceled, "canceledBy"));

        (HttpStatusCode claimCanceled, JsonDocument _) = await ClaimTaskRawAsync(
            secondTask, rowVersion: 1).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, claimCanceled);
    }

    [Fact]
    public async Task CompleteClinicalDocument_ClosesMatchingOpenTask()
    {
        ClinicalWorkspace workspace = await clinical.BuildWorkspaceAsync("taskdoc")
            .ConfigureAwait(false);
        const string workflowCode = "task-doc-flow";
        _ = await PublishWorkflowAsync(
            workflowCode,
            TaskDocumentFlow(workspace.DocumentDefinitionCode))
            .ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            workflowCode,
            subjectType: "encounter",
            workspace.EncounterId).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0)
            .ConfigureAwait(false);
        Guid taskId = await GetSingleTaskIdAsync(pipelineId).ConfigureAwait(false);

        using (JsonDocument before = await GetTaskAsync(taskId).ConfigureAwait(false))
        {
            Assert.Equal("open", Str(before, "status"));
        }

        using JsonDocument started = await clinical.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var documentId = Guid.Parse(Str(started, "id"));
        using JsonDocument completed = await clinical.CompleteDocumentAsync(
            documentId, rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", Str(completed, "status"));

        using JsonDocument taskAfter = await GetTaskAsync(taskId).ConfigureAwait(false);
        Assert.Equal("completed", Str(taskAfter, "status"));
        Assert.Equal(Actor, Str(taskAfter, "completedBy"));
        Assert.NotEqual(
            JsonValueKind.Null,
            taskAfter.RootElement.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task PipelineCancelEnterInErrorAndEndNode_CancelOpenTasks()
    {
        _ = await PublishWorkflowAsync(
            "task-cancel-flow",
            WorkflowTestSchemas.WithTaskNode()).ConfigureAwait(false);

        Guid canceledPatient = await SeedPatientAsync("cancel-a").ConfigureAwait(false);
        Guid canceledPipeline = await StartPipelineIdAsync(
            "task-cancel-flow",
            subjectType: "patient",
            canceledPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(canceledPipeline, rowVersion: 0)
            .ConfigureAwait(false);
        Guid canceledTask = await GetSingleTaskIdAsync(canceledPipeline).ConfigureAwait(false);
        _ = await CancelPipelineAsync(canceledPipeline, rowVersion: 1, reason: "Stopped")
            .ConfigureAwait(false);
        using (JsonDocument canceled = await GetTaskAsync(canceledTask).ConfigureAwait(false))
        {
            Assert.Equal("canceled", Str(canceled, "status"));
            Assert.Equal(Actor, Str(canceled, "canceledBy"));
        }

        Guid erroredPatient = await SeedPatientAsync("cancel-b").ConfigureAwait(false);
        Guid erroredPipeline = await StartPipelineIdAsync(
            "task-cancel-flow",
            subjectType: "patient",
            erroredPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(erroredPipeline, rowVersion: 0)
            .ConfigureAwait(false);
        Guid erroredTask = await GetSingleTaskIdAsync(erroredPipeline).ConfigureAwait(false);
        _ = await EnterInErrorPipelineAsync(erroredPipeline, rowVersion: 1)
            .ConfigureAwait(false);
        using (JsonDocument errored = await GetTaskAsync(erroredTask).ConfigureAwait(false))
        {
            Assert.Equal("canceled", Str(errored, "status"));
        }

        Guid endPatient = await SeedPatientAsync("cancel-c").ConfigureAwait(false);
        Guid endNodePipeline = await StartPipelineIdAsync(
            "task-cancel-flow",
            subjectType: "patient",
            endPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(endNodePipeline, rowVersion: 0)
            .ConfigureAwait(false);
        Guid endNodeTask = await GetSingleTaskIdAsync(endNodePipeline).ConfigureAwait(false);
        using JsonDocument completedPipeline = await AdvancePipelineAsync(
            endNodePipeline, rowVersion: 1).ConfigureAwait(false);
        Assert.Equal("completed", Str(completedPipeline, "status"));
        using JsonDocument afterEnd = await GetTaskAsync(endNodeTask).ConfigureAwait(false);
        Assert.Equal("canceled", Str(afterEnd, "status"));
    }

    [Fact]
    public async Task List_FiltersByStatusAndAssignee()
    {
        _ = await PublishWorkflowAsync(
            "task-list-flow",
            WorkflowTestSchemas.WithTaskNode()).ConfigureAwait(false);
        Guid firstPatient = await SeedPatientAsync("list-a").ConfigureAwait(false);
        Guid first = await StartPipelineIdAsync(
            "task-list-flow",
            subjectType: "patient",
            firstPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(first, rowVersion: 0).ConfigureAwait(false);
        Guid firstTask = await GetSingleTaskIdAsync(first).ConfigureAwait(false);

        Guid secondPatient = await SeedPatientAsync("list-b").ConfigureAwait(false);
        Guid second = await StartPipelineIdAsync(
            "task-list-flow",
            subjectType: "patient",
            secondPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(second, rowVersion: 0).ConfigureAwait(false);
        Guid secondTask = await GetSingleTaskIdAsync(second).ConfigureAwait(false);
        _ = await ClaimTaskAsync(secondTask, rowVersion: 0).ConfigureAwait(false);

        using (JsonDocument byStatus = await ListTasksAsync("status=open")
            .ConfigureAwait(false))
        {
            JsonElement[] openTasks = [.. byStatus.RootElement
                .GetProperty("tasks")
                .EnumerateArray()];
            Assert.Single(openTasks);
            Assert.Equal(
                firstTask.ToString("D", CultureInfo.InvariantCulture),
                Str(openTasks[0], "id"));
        }

        using (JsonDocument byRole = await ListTasksAsync("assignedRole=nurse")
            .ConfigureAwait(false))
        {
            Assert.Equal(2, byRole.RootElement.GetProperty("tasks").GetArrayLength());
        }

        using JsonDocument byStatusAndRole = await ListTasksAsync(
            "status=open&assignedRole=nurse").ConfigureAwait(false);
        Assert.Equal(
            1,
            byStatusAndRole.RootElement.GetProperty("tasks").GetArrayLength());
    }

    [Fact]
    public async Task Get_UnknownAndCrossTenantTask_NotFound()
    {
        _ = await PublishWorkflowAsync(
            "task-tenant-flow",
            WorkflowTestSchemas.WithTaskNode()).ConfigureAwait(false);
        Guid tenantPatient = await SeedPatientAsync("tenant").ConfigureAwait(false);
        Guid pipelineId = await StartPipelineIdAsync(
            "task-tenant-flow",
            subjectType: "patient",
            tenantPatient).ConfigureAwait(false);
        _ = await AdvancePipelineAsync(pipelineId, rowVersion: 0)
            .ConfigureAwait(false);
        Guid taskId = await GetSingleTaskIdAsync(pipelineId).ConfigureAwait(false);

        (HttpStatusCode unknownStatus, JsonDocument _) = await GetTaskRawAsync(
            Guid.NewGuid()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);

        await factory.SeedSecondaryHospitalAsync().ConfigureAwait(false);
        using HttpClient secondaryClient = factory.CreateClient();
        secondaryClient.AcceptJsonApi();
        secondaryClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", SecondaryHospitalCode);
        using HttpResponseMessage crossTenant = await secondaryClient.GetAsync(
            new Uri($"/api/tasks/{taskId:D}", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
    }

    [Fact]
    public async Task RepublishedAssigneeAndDueDays_DoNotAffectPinnedPipelineTasks()
    {
        _ = await PublishWorkflowAsync(
            "task-republish",
            WorkflowTestSchemas.WithTaskNode()).ConfigureAwait(false);
        await PublishNextVersionAsync(
            "task-republish",
            TaskNodeVariant(role: "physician", dueDays: 7)).ConfigureAwait(false);

        Guid oldPatient = await SeedPatientAsync("republish-a").ConfigureAwait(false);
        Guid oldPipeline = await StartPipelineIdAsync(
            "task-republish",
            subjectType: "patient",
            oldPatient,
            workflowVersion: "1.0.0").ConfigureAwait(false);
        _ = await AdvancePipelineAsync(oldPipeline, rowVersion: 0)
            .ConfigureAwait(false);
        Guid oldTask = await GetSingleTaskIdAsync(oldPipeline).ConfigureAwait(false);
        using (JsonDocument oldSnapshot = await GetTaskAsync(oldTask).ConfigureAwait(false))
        {
            Assert.Equal("nurse", Str(oldSnapshot, "assignedRole"));
            var oldDueAt = DateTimeOffset.Parse(
                Str(oldSnapshot, "dueAt"), CultureInfo.InvariantCulture);
            Assert.InRange(
                (oldDueAt - DateTimeOffset.UtcNow).TotalDays,
                2.9,
                3.1);
        }

        Guid newPatient = await SeedPatientAsync("republish-b").ConfigureAwait(false);
        Guid newPipeline = await StartPipelineIdAsync(
            "task-republish",
            subjectType: "patient",
            newPatient,
            workflowVersion: "1.0.1").ConfigureAwait(false);
        _ = await AdvancePipelineAsync(newPipeline, rowVersion: 0)
            .ConfigureAwait(false);
        Guid newTask = await GetSingleTaskIdAsync(newPipeline).ConfigureAwait(false);
        using (JsonDocument newSnapshot = await GetTaskAsync(newTask).ConfigureAwait(false))
        {
            Assert.Equal("physician", Str(newSnapshot, "assignedRole"));
            var newDueAt = DateTimeOffset.Parse(
                Str(newSnapshot, "dueAt"), CultureInfo.InvariantCulture);
            Assert.InRange(
                (newDueAt - DateTimeOffset.UtcNow).TotalDays,
                6.9,
                7.1);
        }

        using JsonDocument oldAfter = await GetTaskAsync(oldTask).ConfigureAwait(false);
        Assert.Equal("nurse", Str(oldAfter, "assignedRole"));
    }

    private async Task<Guid> SeedPatientAsync(string suffix)
    {
        return await clinical.CreatePatientAsync(
            $"MRN-{suffix}",
            "Ada",
            "Lovelace",
            "1990-01-01",
            "female").ConfigureAwait(false);
    }

    private async Task<(Guid PatientId, Guid EncounterId)> SeedWorkspaceEncounterAsync(
        string suffix)
    {
        Guid facilityId = await clinical.CreateFacilityAsync(
            $"task-fac-{suffix}", $"Facility {suffix}").ConfigureAwait(false);
        Guid clinicalAreaId = await clinical.CreateClinicalAreaAsync(
            $"task-area-{suffix}", $"Area {suffix}", facilityId).ConfigureAwait(false);
        Guid patientId = await clinical.CreatePatientAsync(
            $"MRN-{suffix}",
            "Ada",
            "Lovelace",
            "1990-01-01",
            "female").ConfigureAwait(false);
        Guid encounterId = await clinical.CreateEncounterAsync(
            patientId,
            facilityId,
            clinicalAreaId,
            "ambulatory",
            "dr-who").ConfigureAwait(false);
        return (patientId, encounterId);
    }

    private async Task<Guid> StartPipelineIdAsync(
        string workflowCode,
        string subjectType,
        Guid? subjectId = null,
        string? workflowVersion = null)
    {
        using JsonDocument started = await StartPipelineAsync(
            workflowCode,
            workflowVersion,
            subjectType,
            subjectId).ConfigureAwait(false);
        return Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!,
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
        Guid? subjectId)
    {
        Guid resolvedSubjectId = subjectId ?? Guid.NewGuid();
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

    private async Task<JsonDocument> ListTasksAsync(string? query = null)
    {
        string path = query is null ? "/api/tasks" : $"/api/tasks?{query}";
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (_, JsonDocument body) = await ReadBodyAsync(response).ConfigureAwait(false);
        return body;
    }

    private async Task<JsonDocument> GetTaskAsync(Guid taskId)
    {
        (HttpStatusCode status, JsonDocument document) = await GetTaskRawAsync(taskId)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> GetTaskRawAsync(
        Guid taskId)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/api/tasks/{taskId:D}", UriKind.Relative)).ConfigureAwait(false);
        return await ReadBodyAsync(response).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ClaimTaskAsync(Guid taskId, uint rowVersion)
    {
        (HttpStatusCode status, JsonDocument document) = await ClaimTaskRawAsync(
            taskId, rowVersion).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> ClaimTaskRawAsync(
        Guid taskId,
        uint rowVersion)
    {
        return await PostJsonAsync(
            $"/api/tasks/{taskId:D}/claim",
            new { rowVersion }).ConfigureAwait(false);
    }

    private async Task<JsonDocument> CompleteTaskAsync(
        Guid taskId,
        uint rowVersion,
        string? reason = null)
    {
        (HttpStatusCode status, JsonDocument document) = await CompleteTaskRawAsync(
            taskId, rowVersion, reason).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> CompleteTaskRawAsync(
        Guid taskId,
        uint rowVersion,
        string? reason = null)
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
            $"/api/tasks/{taskId:D}/complete",
            payload).ConfigureAwait(false);
    }

    private async Task<JsonDocument> CancelTaskAsync(
        Guid taskId,
        uint rowVersion,
        string? reason = null)
    {
        (HttpStatusCode status, JsonDocument document) = await CancelTaskRawAsync(
            taskId, rowVersion, reason).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status);
        return document;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> CancelTaskRawAsync(
        Guid taskId,
        uint rowVersion,
        string? reason = null)
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
            $"/api/tasks/{taskId:D}/cancel",
            payload).ConfigureAwait(false);
    }

    private async Task<Guid> GetSingleTaskIdAsync(Guid pipelineId)
    {
        using JsonDocument list = await ListTasksAsync(
            $"pipelineId={pipelineId:D}").ConfigureAwait(false);
        JsonElement task = Assert.Single(list.RootElement.GetProperty("tasks").EnumerateArray());
        return Guid.Parse(Str(task, "id"));
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

    private async Task PublishNextVersionAsync(string code, string workflowSchemaJson)
    {
        string definitionId = await FindDefinitionIdAsync(code).ConfigureAwait(false);
        using HttpResponseMessage created = await client.PostAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string draftId = await GetDraftIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument updated = await api.PatchResourceAsync(
            "workflowVersions",
            draftId,
            new
            {
                workflowSchemaJson,
                rowVersion,
            }).ConfigureAwait(false);
        using JsonDocument inReview = await PostVersionActionAsync(
            draftId,
            "submit-review",
            JsonApiClient.AttrUInt(updated, "rowVersion")).ConfigureAwait(false);
        _ = await PostVersionActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
    }

    private async Task<string> CreateDefinitionAsync(
        string code,
        string name,
        string workflowSchemaJson)
    {
        using JsonDocument created = await api.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code,
                name,
                initialWorkflowSchemaJson = workflowSchemaJson,
            }).ConfigureAwait(false);
        return JsonApiClient.RequireId(created);
    }

    private async Task<string> FindDefinitionIdAsync(string code)
    {
        using JsonDocument list = await api.GetAsync(
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
        using JsonDocument definition = await api.GetAsync(
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
        using JsonDocument document = await api.GetAsync(
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
        using HttpResponseMessage response = await client.PostAsync(
            new Uri(
                $"/api/workflowVersions/{versionId}/{action}{suffix}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        return await JsonApiClient.ReadDocumentAsync(response).ConfigureAwait(false);
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

        using HttpResponseMessage response = await client.SendAsync(request)
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

    private static string Str(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetString()!;
    }

    private static string Str(JsonElement element, string name)
    {
        return element.GetProperty(name).GetString()!;
    }

    private static uint UInt(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetUInt32();
    }

    private static string TaskDocumentFlow(string formCode)
    {
        return $$"""
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                {
                  "id": "fill-doc",
                  "type": "task",
                  "name": "Fill document",
                  "assignee": { "role": "nurse" },
                  "formCode": "{{formCode}}",
                  "formVersion": "1.0.0"
                },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "fill-doc" },
                { "from": "fill-doc", "to": "end" }
              ]
            }
            """;
    }

    private static string TaskNodeVariant(string role, int dueDays)
    {
        return $$"""
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                {
                  "id": "admit-task",
                  "type": "task",
                  "name": "Admission assessment",
                  "assignee": { "role": "{{role}}" },
                  "formCode": "admission-assessment",
                  "formVersion": "1.0.0",
                  "dueDays": {{dueDays.ToString(CultureInfo.InvariantCulture)}}
                },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "admit-task" },
                { "from": "admit-task", "to": "end" }
              ]
            }
            """;
    }
}
