using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Cynara.Api.Tests.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;

namespace Cynara.Api.Tests.Capabilities;

/// <summary>
/// End-to-end capability enforcement for the Stage 3 workflow surface
/// (workflow config, pipeline actions, task actions) with the real
/// effective-capability resolver driving MVC filters, authorization
/// middleware, and the auditing result handler. Coverage: allowed / denied /
/// write-denied / cross-tenant isolation / audit.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowCapabilityEnforcementTests : IAsyncDisposable
{
    private const string PrimaryHospitalCode =
        CynaraTenantWebApplicationFactory.PrimaryCode;

    private const string OtherHospitalCode =
        CynaraTenantWebApplicationFactory.OtherCode;

    private const string Admin = "wf-admin";
    private const string Doctor = "wf-doctor";
    private const string Nurse = "wf-nurse";

    public WorkflowCapabilityEnforcementTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(
            database.Settings,
            grantAllCapabilities: false);
        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListWorkflowDefinitions_Returns403_WhenActorHoldsNoGrant()
    {
        HttpClient client = CreateClient(Nurse, PrimaryHospitalCode);
        int deniedBefore = await DenialCountAsync().ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/workflowDefinitions", UriKind.Relative))
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        Assert.True(
            await DenialCountAsync().ConfigureAwait(false) > deniedBefore,
            "Expected an access.denied audit event.");
    }

    [Fact]
    public async Task CreateWorkflowDefinition_Returns403_WhenActorHoldsOnlyRead()
    {
        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.WorkflowsRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await PostJsonApiAsync(
            client,
            "/api/workflowDefinitions",
            WorkflowDefinitionPayload(
                "wf-readonly",
                WorkflowTestSchemas.Minimal())).ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task CreateWorkflowDefinition_ReturnsCreated_WhenActorHoldsWrite()
    {
        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.WorkflowsRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.WorkflowsWrite,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await PostJsonApiAsync(
            client,
            "/api/workflowDefinitions",
            WorkflowDefinitionPayload(
                "wf-create",
                WorkflowTestSchemas.Minimal())).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SubmitReview_Returns403_WhenActorHoldsOnlyRead()
    {
        JsonApiClient adminApi = await CreateAdminApiAsync().ConfigureAwait(false);
        Guid draftId = await CreateDraftAsync(adminApi, "wf-review-gate")
            .ConfigureAwait(false);
        uint rowVersion = await GetRowVersionAsync(adminApi, draftId)
            .ConfigureAwait(false);

        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.WorkflowsRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        int deniedBefore = await DenialCountAsync().ConfigureAwait(false);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(
                $"/api/workflowVersions/{draftId}/submit-review"
                + $"?rowVersion={rowVersion.ToString(CultureInfo.InvariantCulture)}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        Assert.True(
            await DenialCountAsync().ConfigureAwait(false) > deniedBefore,
            "Expected an access.denied audit event.");
    }

    [Fact]
    public async Task WorkflowDefinition_IsNotVisibleInAnotherHospital_WhenOnlyGrantedThere()
    {
        JsonApiClient adminApi = await CreateAdminApiAsync().ConfigureAwait(false);
        using JsonDocument created = await adminApi.PostResourceAsync(
            "workflowDefinitions",
            WorkflowDefinitionAttributes(
                "wf-cross-tenant",
                WorkflowTestSchemas.Minimal())).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);

        await Factory.SeedSecondaryHospitalAsync().ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.WorkflowsRead,
            OtherHospitalCode).ConfigureAwait(false);
        HttpClient otherClient = CreateClient(Doctor, OtherHospitalCode);

        using HttpResponseMessage denied = await otherClient
            .GetAsync(
                new Uri($"/api/workflowDefinitions/{definitionId}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task ListPipelines_Returns403_WhenActorHoldsNoGrant_AndAuditsDenial()
    {
        HttpClient client = CreateClient(Nurse, PrimaryHospitalCode);
        int deniedBefore = await DenialCountAsync().ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/pipelines", UriKind.Relative))
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        Assert.True(
            await DenialCountAsync().ConfigureAwait(false) > deniedBefore,
            "Expected an access.denied audit event from the authorization "
            + "middleware result handler.");
    }

    [Fact]
    public async Task ListPipelines_Returns200_WhenActorHoldsRead()
    {
        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/pipelines", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartPipeline_Returns403_WhenActorHoldsOnlyRead()
    {
        Guid encounterId = await SeedPublishedWorkflowAndEncounterAsync(
            "wf-pipeline-start-deny").ConfigureAwait(false);

        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        int deniedBefore = await DenialCountAsync().ConfigureAwait(false);

        using HttpResponseMessage response = await PostStartPipelineAsync(
            client,
            "wf-pipeline-start-deny",
            encounterId).ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        Assert.True(
            await DenialCountAsync().ConfigureAwait(false) > deniedBefore,
            "Expected an access.denied audit event.");
    }

    [Fact]
    public async Task StartPipeline_ReturnsCreated_WhenActorHoldsWrite()
    {
        Guid encounterId = await SeedPublishedWorkflowAndEncounterAsync(
            "wf-pipeline-start-ok").ConfigureAwait(false);

        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesWrite,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await PostStartPipelineAsync(
            client,
            "wf-pipeline-start-ok",
            encounterId).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ListTasks_Returns403_WhenActorHoldsNoGrant()
    {
        HttpClient client = CreateClient(Nurse, PrimaryHospitalCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/tasks", UriKind.Relative))
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task ListTasks_Returns200_WhenActorHoldsRead()
    {
        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.TasksRead,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri("/api/tasks", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdvancePipeline_Returns200_WhenActorHoldsWrite()
    {
        (Guid pipelineId, uint rowVersion) = await SeedStartedPipelineAsync(
            "wf-pipeline-advance-ok",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesWrite,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await PostPipelineTransitionAsync(
            client,
            pipelineId,
            "advance",
            rowVersion).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PipelineMutations_Return403_WhenActorHoldsOnlyRead_AndAuditDenial()
    {
        (Guid pipelineId, uint rowVersion) = await SeedStartedPipelineAsync(
            "wf-pipeline-mutations-deny",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        int deniedBefore = await DenialCountAsync().ConfigureAwait(false);

        foreach (string action in new[]
        {
            "advance",
            "complete",
            "cancel",
            "enter-in-error",
        })
        {
            using HttpResponseMessage response = await PostPipelineTransitionAsync(
                client,
                pipelineId,
                action,
                rowVersion).ConfigureAwait(false);
            await AssertForbiddenAsync(response).ConfigureAwait(false);
        }

        Assert.True(
            await DenialCountAsync().ConfigureAwait(false) > deniedBefore,
            "Expected an access.denied audit event per denied pipeline "
            + "mutation.");
    }

    [Fact]
    public async Task ClaimTask_Returns200_WhenActorHoldsWrite()
    {
        Guid taskId = await SeedStartedTaskAsync("wf-task-claim-ok")
            .ConfigureAwait(false);

        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.TasksRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.TasksWrite,
            PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await PostTaskTransitionAsync(
            client,
            taskId,
            "claim",
            rowVersion: 0).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TaskMutations_Return403_WhenActorHoldsOnlyRead_AndAuditDenial()
    {
        Guid taskId = await SeedStartedTaskAsync("wf-task-mutations-deny")
            .ConfigureAwait(false);

        HttpClient client = CreateClient(Doctor, PrimaryHospitalCode);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.TasksRead,
            PrimaryHospitalCode).ConfigureAwait(false);
        int deniedBefore = await DenialCountAsync().ConfigureAwait(false);

        foreach (string action in new[] { "claim", "complete", "cancel" })
        {
            using HttpResponseMessage response = await PostTaskTransitionAsync(
                client,
                taskId,
                action,
                rowVersion: 0).ConfigureAwait(false);
            await AssertForbiddenAsync(response).ConfigureAwait(false);
        }

        Assert.True(
            await DenialCountAsync().ConfigureAwait(false) > deniedBefore,
            "Expected an access.denied audit event per denied task mutation.");
    }

    [Fact]
    public async Task Pipeline_IsNotVisibleInAnotherHospital_WhenOnlyGrantedThere()
    {
        (Guid pipelineId, _) = await SeedStartedPipelineAsync(
            "wf-pipeline-cross-tenant",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        await Factory.SeedSecondaryHospitalAsync().ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.PipelinesRead,
            OtherHospitalCode).ConfigureAwait(false);
        HttpClient otherClient = CreateClient(Doctor, OtherHospitalCode);

        using HttpResponseMessage denied = await otherClient
            .GetAsync(new Uri($"/api/pipelines/{pipelineId:D}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task Task_IsNotVisibleInAnotherHospital_WhenOnlyGrantedThere()
    {
        Guid taskId = await SeedStartedTaskAsync("wf-task-cross-tenant")
            .ConfigureAwait(false);

        await Factory.SeedSecondaryHospitalAsync().ConfigureAwait(false);
        await SeedAssignmentAsync(
            Doctor,
            CapabilityCodes.TasksRead,
            OtherHospitalCode).ConfigureAwait(false);
        HttpClient otherClient = CreateClient(Doctor, OtherHospitalCode);

        using HttpResponseMessage denied = await otherClient
            .GetAsync(new Uri($"/api/tasks/{taskId:D}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    private async Task<JsonApiClient> CreateAdminApiAsync()
    {
        HttpClient adminClient = CreateClient(Admin, PrimaryHospitalCode);
        await SeedAllCapabilitiesAsync(Admin, PrimaryHospitalCode)
            .ConfigureAwait(false);
        var adminApi = new JsonApiClient(adminClient);
        adminApi.UseHospitalContext(PrimaryHospitalCode);
        return adminApi;
    }

    private async Task<Guid> SeedPublishedWorkflowAndEncounterAsync(string code)
    {
        JsonApiClient adminApi = await CreateAdminApiAsync().ConfigureAwait(false);
        await PublishWorkflowAsync(
            adminApi,
            code,
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        return await SeedEncounterAsync(adminApi.Http).ConfigureAwait(false);
    }

    private static async Task<Guid> CreateDraftAsync(
        JsonApiClient api,
        string code)
    {
        using JsonDocument created = await api.PostResourceAsync(
            "workflowDefinitions",
            WorkflowDefinitionAttributes(
                code,
                WorkflowTestSchemas.Minimal())).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);
        return await FindDraftIdAsync(api, definitionId).ConfigureAwait(false);
    }

    private static async Task PublishWorkflowAsync(
        JsonApiClient api,
        string code,
        string workflowSchemaJson)
    {
        using JsonDocument created = await api.PostResourceAsync(
            "workflowDefinitions",
            WorkflowDefinitionAttributes(
                code,
                workflowSchemaJson)).ConfigureAwait(false);
        string definitionId = JsonApiClient.RequireId(created);
        Guid draftId = await FindDraftIdAsync(api, definitionId)
            .ConfigureAwait(false);

        uint rowVersion = await GetRowVersionAsync(api, draftId)
            .ConfigureAwait(false);
        using JsonDocument inReview = await PostVersionActionAsync(
            api,
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        _ = await PostVersionActionAsync(
            api,
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
    }

    private static async Task<Guid> FindDraftIdAsync(
        JsonApiClient api,
        string definitionId)
    {
        using JsonDocument definition = await api.GetAsync(
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
        return Guid.Parse(draftId, CultureInfo.InvariantCulture);
    }

    private static async Task<uint> GetRowVersionAsync(
        JsonApiClient api,
        Guid versionId)
    {
        using JsonDocument document = await api.GetAsync(
            $"/api/workflowVersions/{versionId}").ConfigureAwait(false);
        return JsonApiClient.AttrUInt(document, "rowVersion");
    }

    private static async Task<JsonDocument> PostVersionActionAsync(
        JsonApiClient api,
        Guid versionId,
        string action,
        uint? rowVersion)
    {
        string suffix = rowVersion is null
            ? string.Empty
            : $"?rowVersion={rowVersion.Value.ToString(CultureInfo.InvariantCulture)}";
        using HttpResponseMessage response = await api.Http.PostAsync(
            new Uri(
                $"/api/workflowVersions/{versionId}/{action}{suffix}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        return await JsonApiClient.ReadDocumentAsync(response)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> SeedEncounterAsync(HttpClient client)
    {
        Guid facilityId = await CreatePlainAsync(
            client,
            "/api/facilities",
            new
            {
                code = $"fac-{Guid.NewGuid():N}",
                name = "Facility",
            }).ConfigureAwait(false);
        Guid clinicalAreaId = await CreatePlainAsync(
            client,
            "/api/clinicalAreas",
            new
            {
                code = $"area-{Guid.NewGuid():N}",
                name = "Area",
                facilityId,
            }).ConfigureAwait(false);
        Guid patientId = await CreatePlainAsync(
            client,
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
            client,
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

    private static async Task<Guid> CreatePlainAsync(
        HttpClient client,
        string path,
        object body)
    {
        using HttpResponseMessage response = await client.PostAsync(
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

    private static async Task<HttpResponseMessage> PostStartPipelineAsync(
        HttpClient client,
        string workflowCode,
        Guid encounterId)
    {
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
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static object WorkflowDefinitionAttributes(
        string code,
        string schemaJson)
    {
        return new
        {
            code,
            name = code,
            initialWorkflowSchemaJson = schemaJson,
        };
    }

    private static object WorkflowDefinitionPayload(
        string code,
        string schemaJson)
    {
        return new
        {
            data = new
            {
                type = "workflowDefinitions",
                attributes = WorkflowDefinitionAttributes(code, schemaJson),
            },
        };
    }

    /// <summary>
    /// JsonApiDotNetCore rejects media-type parameters such as charset=utf-8,
    /// so the content type is set explicitly without encoding parameters.
    /// </summary>
    private static async Task<HttpResponseMessage> PostJsonApiAsync(
        HttpClient client,
        string path,
        object payload)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8);

        content.Headers.ContentType = new MediaTypeHeaderValue(
            JsonApiMedia.ContentType);
        return await client
            .PostAsync(new Uri(path, UriKind.Relative), content)
            .ConfigureAwait(false);
    }

    private HttpClient CreateClient(string? actorId, string hospitalCode)
    {
        HttpClient client = Factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code",
            hospitalCode);
        if (actorId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Actor-Id",
                actorId);
        }

        return client;
    }

    private async Task SeedAssignmentAsync(
        string actorId,
        string capability,
        string hospitalCode)
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Factory.CreateScope();
        Hospital hospital = await scope
            .LoadHospitalAsync(hospitalCode)
            .ConfigureAwait(false);
        scope.DbContext.CapabilityAssignments.Add(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospital.Id,
            ActorId = actorId,
            Capability = capability,
            AssignedAt = DateTimeOffset.UtcNow,
        });
        _ = await scope.DbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task SeedAllCapabilitiesAsync(
        string actorId,
        string hospitalCode)
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Factory.CreateScope();
        Hospital hospital = await scope
            .LoadHospitalAsync(hospitalCode)
            .ConfigureAwait(false);
        foreach (string capability in CapabilityCodes.All)
        {
            scope.DbContext.CapabilityAssignments.Add(new CapabilityAssignment
            {
                Id = Guid.NewGuid(),
                HospitalId = hospital.Id,
                ActorId = actorId,
                Capability = capability,
                AssignedAt = DateTimeOffset.UtcNow,
            });
        }

        _ = await scope.DbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"Expected 403, got {(int)response.StatusCode}: {body}");
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            message);
        using var document = JsonDocument.Parse(body);
        JsonElement errors = document.RootElement.GetProperty("errors");
        JsonElement error = Assert.Single(errors.EnumerateArray());
        Assert.Equal(
            "403",
            error.GetProperty("status").GetString());
        Assert.Equal(
            "Capability required",
            error.GetProperty("title").GetString());
    }

    private async Task<(Guid PipelineId, uint RowVersion)> SeedStartedPipelineAsync(
        string workflowCode,
        string workflowSchemaJson)
    {
        JsonApiClient adminApi = await CreateAdminApiAsync().ConfigureAwait(false);
        await PublishWorkflowAsync(
            adminApi,
            workflowCode,
            workflowSchemaJson).ConfigureAwait(false);
        Guid encounterId = await SeedEncounterAsync(adminApi.Http)
            .ConfigureAwait(false);

        using HttpResponseMessage response = await PostStartPipelineAsync(
            adminApi.Http,
            workflowCode,
            encounterId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var pipelineId = Guid.Parse(
            document.RootElement.GetProperty("id").GetString()!,
            CultureInfo.InvariantCulture);
        uint rowVersion = document.RootElement
            .GetProperty("rowVersion")
            .GetUInt32();
        return (pipelineId, rowVersion);
    }

    private async Task<Guid> SeedStartedTaskAsync(string workflowCode)
    {
        (Guid pipelineId, uint rowVersion) = await SeedStartedPipelineAsync(
            workflowCode,
            WorkflowTestSchemas.WithTaskNode()).ConfigureAwait(false);

        HttpClient adminClient = CreateClient(Admin, PrimaryHospitalCode);
        using HttpResponseMessage advanced = await PostPipelineTransitionAsync(
            adminClient,
            pipelineId,
            "advance",
            rowVersion).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        using HttpResponseMessage list = await adminClient.GetAsync(
            new Uri("/api/tasks", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var document = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync().ConfigureAwait(false));
        string taskId = document.RootElement.GetProperty("tasks")
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("pipelineId").GetString(),
                pipelineId.ToString("D", CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
            .GetProperty("id")
            .GetString()!;
        return Guid.Parse(taskId, CultureInfo.InvariantCulture);
    }

    private static async Task<HttpResponseMessage> PostPipelineTransitionAsync(
        HttpClient client,
        Guid pipelineId,
        string action,
        uint rowVersion)
    {
        return await PostJsonAsync(
            client,
            $"/api/pipelines/{pipelineId:D}/{action}",
            new { rowVersion }).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> PostTaskTransitionAsync(
        HttpClient client,
        Guid taskId,
        string action,
        uint rowVersion)
    {
        return await PostJsonAsync(
            client,
            $"/api/tasks/{taskId:D}/{action}",
            new { rowVersion }).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string path,
        object body)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private async Task<int> DenialCountAsync()
    {
        await using AsyncServiceScope scope = Factory.Services
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .Where(item => item.Action == "access.denied")
            .CountAsync()
            .ConfigureAwait(false);
    }
}
