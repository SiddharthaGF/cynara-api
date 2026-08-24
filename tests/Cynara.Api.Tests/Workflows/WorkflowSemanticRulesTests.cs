using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// Verifies the WORK-001..WORK-016 semantic rules enforced on workflow
/// schemas beyond the structural JSON Schema contract. Invalid graphs are
/// rejected with 400 Bad Request and a message naming the violated rule code.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowSemanticRulesTests : IDisposable
{
    public WorkflowSemanticRulesTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new WorkflowSemanticTestApplicationFactory(database.Settings);
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

    [Theory]
    [InlineData(nameof(WorkflowTestSchemas.WithMissingStart), "ENTRY_REQUIRED")]
    [InlineData(nameof(WorkflowTestSchemas.WithMissingEnd), "EXIT_REQUIRED")]
    [InlineData(nameof(WorkflowTestSchemas.WithDuplicateNodeId), "DUPLICATE_NODE_ID")]
    [InlineData(nameof(WorkflowTestSchemas.WithCycle), "CYCLE_DETECTED")]
    [InlineData(nameof(WorkflowTestSchemas.WithUnreachableNode), "UNREACHABLE_NODE")]
    [InlineData(nameof(WorkflowTestSchemas.WithUnknownEdgeNode), "EDGE_UNKNOWN_NODE")]
    [InlineData(nameof(WorkflowTestSchemas.WithUnknownConditionRef), "CONDITION_UNKNOWN_REF")]
    [InlineData(nameof(WorkflowTestSchemas.WithStartIncomingEdge), "ENTRY_INCOMING_EDGE")]
    [InlineData(nameof(WorkflowTestSchemas.WithTaskConditionalOutput), "TASK_UNCONDITIONAL_OUTPUT")]
    public async Task InvalidWorkflow_IsRejectedWithRuleCode(
        string schemaFactory,
        string expectedCode)
    {
        string workflowSchema = InvokeSchemaFactory(schemaFactory);
        string detail = await CreateExpectingBadRequestAsync(workflowSchema)
            .ConfigureAwait(false);

        Assert.Contains(expectedCode, detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decision_WithConditionalAndDefaultEdge_IsAccepted()
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "workflowDefinitions",
            new
            {
                code = "decision-flow",
                name = "Decision flow",
                initialWorkflowSchemaJson = WorkflowTestSchemas.WithDecision(),
            }).ConfigureAwait(false);
        Assert.Equal(
            "decision-flow",
            JsonApiClient.AttrString(created, "code"));
    }

    [Fact]
    public async Task Publish_RequiresFormVersionPinWhenFormCodeSet()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "pinned-form-workflow",
            "Pinned form workflow",
            WorkflowTestSchemas.WithPinnedFormTask(formVersion: null))
            .ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);

        using JsonDocument inReview = await SubmitReviewAsync(draftId, rowVersion)
            .ConfigureAwait(false);

        using HttpResponseMessage publish = await Client.PostAsync(
            new Uri(
                $"/api/workflowVersions/{draftId}/publish?rowVersion={JsonApiClient.AttrUInt(inReview, "rowVersion").ToString(CultureInfo.InvariantCulture)}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
        string publishBody = await publish.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("FORM_VERSION_REQUIRED", publishBody, StringComparison.Ordinal);

        using HttpResponseMessage withdraw = await Client.PostAsync(
            new Uri(
                $"/api/workflowVersions/{draftId}/withdraw-review?rowVersion={JsonApiClient.AttrUInt(inReview, "rowVersion").ToString(CultureInfo.InvariantCulture)}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        using JsonDocument withdrawn = await JsonApiClient.ReadDocumentAsync(withdraw)
            .ConfigureAwait(false);

        await PatchDraftAsync(
            draftId,
            WorkflowTestSchemas.WithPinnedFormTask(formVersion: "1.0.0"),
            JsonApiClient.AttrUInt(withdrawn, "rowVersion")).ConfigureAwait(false);
        using JsonDocument republishedReview = await SubmitReviewAsync(
            draftId,
            JsonApiClient.AttrUInt(withdrawn, "rowVersion") + 1).ConfigureAwait(false);
        using HttpResponseMessage success = await Client.PostAsync(
            new Uri(
                $"/api/workflowVersions/{draftId}/publish?rowVersion={JsonApiClient.AttrUInt(republishedReview, "rowVersion").ToString(CultureInfo.InvariantCulture)}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private WorkflowSemanticTestApplicationFactory Factory { get; }

    private static string InvokeSchemaFactory(string name)
    {
        return name switch
        {
            nameof(WorkflowTestSchemas.WithMissingStart) => WorkflowTestSchemas.WithMissingStart(),
            nameof(WorkflowTestSchemas.WithMissingEnd) => WorkflowTestSchemas.WithMissingEnd(),
            nameof(WorkflowTestSchemas.WithDuplicateNodeId) => WorkflowTestSchemas.WithDuplicateNodeId(),
            nameof(WorkflowTestSchemas.WithCycle) => WorkflowTestSchemas.WithCycle(),
            nameof(WorkflowTestSchemas.WithUnreachableNode) => WorkflowTestSchemas.WithUnreachableNode(),
            nameof(WorkflowTestSchemas.WithUnknownEdgeNode) => WorkflowTestSchemas.WithUnknownEdgeNode(),
            nameof(WorkflowTestSchemas.WithUnknownConditionRef) => WorkflowTestSchemas.WithUnknownConditionRef(),
            nameof(WorkflowTestSchemas.WithStartIncomingEdge) => WorkflowTestSchemas.WithStartIncomingEdge(),
            nameof(WorkflowTestSchemas.WithTaskConditionalOutput) => WorkflowTestSchemas.WithTaskConditionalOutput(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown schema factory."),
        };
    }

    private async Task<string> CreateExpectingBadRequestAsync(string workflowSchema)
    {
        using StringContent content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "workflowDefinitions",
                attributes = new
                {
                    code = "invalid-flow-"
                        + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                    name = "Invalid workflow",
                    initialWorkflowSchemaJson = workflowSchema,
                },
            },
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/workflowDefinitions", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage actual = await Client.SendAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, actual.StatusCode);
        return await actual.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private async Task<JsonDocument> SubmitReviewAsync(string versionId, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri(
                $"/api/workflowVersions/{versionId}/submit-review?rowVersion={rowVersion.ToString(CultureInfo.InvariantCulture)}",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        return await JsonApiClient.ReadDocumentAsync(response).ConfigureAwait(false);
    }

    private async Task PatchDraftAsync(
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
        using HttpResponseMessage response = await Client.SendAsync(request)
            .ConfigureAwait(false);
        Assert.True(
            response.IsSuccessStatusCode,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Expected successful patch, got {(int)response.StatusCode}"));
    }
}

internal sealed class WorkflowSemanticTestApplicationFactory(
    TestDatabaseSettings database)
    : CynaraWebApplicationFactory(database);
