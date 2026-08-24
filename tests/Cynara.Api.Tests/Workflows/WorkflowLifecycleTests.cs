using System.Globalization;
using System.Net;
using System.Text.Json;

using Cynara.Domain.Audit;

namespace Cynara.Api.Tests.Workflows;

/// <summary>
/// HTTP-level lifecycle tests for workflow definitions and immutable
/// workflow versions: draft CRUD, draft → review → published → retired state
/// machine, optimistic concurrency, published-version immutability, and audit
/// emission.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowLifecycleTests : IDisposable
{
    public WorkflowLifecycleTests(PostgreSqlDatabaseFixture database)
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
    public async Task Create_SeedsDraftAndPublishesImmutableVersion()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "triage-workflow",
            "Triage workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);
        Assert.Equal(0u, rowVersion);

        using JsonDocument inReview = await PostActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        Assert.Equal("review", JsonApiClient.AttrString(inReview, "status"));
        Assert.Equal(1u, JsonApiClient.AttrUInt(inReview, "rowVersion"));

        using JsonDocument published = await PostActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
        Assert.Equal("published", JsonApiClient.AttrString(published, "status"));
        Assert.Equal("1.0.0", JsonApiClient.AttrString(published, "version"));
        Assert.Equal("1.0.0", JsonApiClient.AttrString(published, "publishedSchemaVersion"));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(published, "contentHash")));
        Assert.NotNull(JsonApiClient.AttrString(published, "publishedAt"));

        await AssertAuditEventsRecordedAsync(
            Guid.Parse(definitionId, CultureInfo.InvariantCulture),
            "workflow.created").ConfigureAwait(false);
        await AssertAuditEventsRecordedAsync(
            Guid.Parse(draftId, CultureInfo.InvariantCulture),
            "workflow.draft.submitted-for-review",
            "workflow.version.published").ConfigureAwait(false);
    }

    [Fact]
    public async Task Publish_NextVersion_IsSequential()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "sequential-workflow",
            "Sequential workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);

        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument inReview = await PostActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        using JsonDocument first = await PostActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
        Assert.Equal("1.0.0", JsonApiClient.AttrString(first, "version"));

        using HttpResponseMessage draftCreated = await Client.PostAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, draftCreated.StatusCode);

        string secondDraftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint secondRowVersion = await Api.GetVersionRowVersionAsync(secondDraftId)
            .ConfigureAwait(false);
        using JsonDocument secondInReview = await PostActionAsync(
            secondDraftId,
            "submit-review",
            secondRowVersion).ConfigureAwait(false);
        using JsonDocument second = await PostActionAsync(
            secondDraftId,
            "publish",
            JsonApiClient.AttrUInt(secondInReview, "rowVersion")).ConfigureAwait(false);
        Assert.Equal("1.0.1", JsonApiClient.AttrString(second, "version"));
    }

    [Fact]
    public async Task SubmitForReview_LocksEditsUntilWithdrawn()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "locked-workflow",
            "Locked workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);

        using JsonDocument inReview = await PostActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);

        using HttpResponseMessage patch = await PatchDraftAsync(
            draftId,
            WorkflowTestSchemas.WithDecision(),
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
        Assert.True(
            patch.StatusCode is HttpStatusCode.Conflict
                or HttpStatusCode.UnprocessableEntity
                or HttpStatusCode.BadRequest,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Expected conflict-style status, got {(int)patch.StatusCode}"));

        using JsonDocument withdrawn = await PostActionAsync(
            draftId,
            "withdraw-review",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);
        Assert.Equal("draft", JsonApiClient.AttrString(withdrawn, "status"));
    }

    [Fact]
    public async Task RejectReview_ReturnsToDraftWithDecision()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "reject-workflow",
            "Reject workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);

        using JsonDocument inReview = await PostActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);

        using JsonDocument rejected = await PostActionAsync(
            draftId,
            "reject-review",
            JsonApiClient.AttrUInt(inReview, "rowVersion"),
            comment: "Missing end node coverage.").ConfigureAwait(false);
        Assert.Equal("draft", JsonApiClient.AttrString(rejected, "status"));
        Assert.Equal("rejected", JsonApiClient.AttrString(rejected, "lastReviewDecision"));
        Assert.Equal(
            "Missing end node coverage.",
            JsonApiClient.AttrString(rejected, "lastReviewComment"));
        Assert.NotNull(JsonApiClient.AttrString(rejected, "lastReviewedAt"));
    }

    [Fact]
    public async Task Retire_KeepsPublishedVersionReadable()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "retire-workflow",
            "Retire workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument inReview = await PostActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        using JsonDocument published = await PostActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);

        using JsonDocument retired = await PostActionAsync(
            draftId,
            "retire",
            rowVersion: null).ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(retired, "status"));
        Assert.NotNull(JsonApiClient.AttrString(retired, "retiredAt"));

        using JsonDocument reRead = await Api.GetAsync(
            $"/api/workflowVersions/{draftId}").ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(reRead, "status"));
        Assert.Equal("1.0.0", JsonApiClient.AttrString(reRead, "version"));
    }

    [Fact]
    public async Task StaleRowVersion_Conflicts()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "stale-workflow",
            "Stale workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);

        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);
        _ = await PatchDraftAsync(
            draftId,
            WorkflowTestSchemas.WithDecision(),
            rowVersion).ConfigureAwait(false);

        using HttpResponseMessage stale = await PatchDraftAsync(
            draftId,
            WorkflowTestSchemas.Minimal(),
            rowVersion).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task DuplicateCode_Conflicts()
    {
        await Api.CreateWorkflowDefinitionAsync(
            "duplicate-workflow",
            "Duplicate workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        using StringContent content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "workflowDefinitions",
                attributes = new
                {
                    code = "duplicate-workflow",
                    name = "Duplicate workflow",
                    initialWorkflowSchemaJson = WorkflowTestSchemas.Minimal(),
                },
            },
        });
        using HttpResponseMessage response = await Client
            .PostAsync(new Uri("/api/workflowDefinitions", UriKind.Relative), content)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SoftDelete_RejectsPublishedAndHidesDeleted()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "soft-delete-workflow",
            "Soft delete workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);

        using HttpResponseMessage softDelete = await Client.DeleteAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/soft-delete-draft",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, softDelete.StatusCode);

        using HttpResponseMessage gone = await Api.SendGetAsync(
            $"/api/workflowDefinitions/{definitionId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task SoftDelete_WithDraftAlongsidePublished_Conflicts()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "soft-delete-published",
            "Soft delete published",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument inReview = await PostActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        _ = await PostActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);

        using HttpResponseMessage draftCreated = await Client.PostAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, draftCreated.StatusCode);

        using HttpResponseMessage softDelete = await Client.DeleteAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/soft-delete-draft",
                UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, softDelete.StatusCode);
    }

    [Fact]
    public async Task CreateDraftFromLatest_CopiesPublishedSchema()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "copy-workflow",
            "Copy workflow",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        uint rowVersion = await Api.GetVersionRowVersionAsync(draftId).ConfigureAwait(false);
        using JsonDocument inReview = await PostActionAsync(
            draftId,
            "submit-review",
            rowVersion).ConfigureAwait(false);
        _ = await PostActionAsync(
            draftId,
            "publish",
            JsonApiClient.AttrUInt(inReview, "rowVersion")).ConfigureAwait(false);

        using HttpResponseMessage draftCreated = await Client.PostAsync(
            new Uri(
                $"/api/workflowDefinitions/{definitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, draftCreated.StatusCode);

        string newDraftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);
        using JsonDocument newDraft = await Api.GetAsync(
            $"/api/workflowVersions/{newDraftId}").ConfigureAwait(false);
        Assert.Equal(
            WorkflowTestSchemas.Minimal(),
            JsonApiClient.AttrString(newDraft, "workflowSchemaJson"));
    }

    [Fact]
    public async Task HardDelete_Endpoints_AreRejected()
    {
        string definitionId = await Api.CreateWorkflowDefinitionAsync(
            "no-hard-delete",
            "No hard delete",
            WorkflowTestSchemas.Minimal()).ConfigureAwait(false);
        string draftId = await Api.GetDraftVersionIdAsync(definitionId).ConfigureAwait(false);

        using HttpResponseMessage definitionDelete = await Api.DeleteAsync(
            $"/api/workflowDefinitions/{definitionId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, definitionDelete.StatusCode);

        using HttpResponseMessage versionDelete = await Api.DeleteAsync(
            $"/api/workflowVersions/{draftId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, versionDelete.StatusCode);
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private WorkflowTestApplicationFactory Factory { get; }

    private async Task<JsonDocument> PostActionAsync(
        string versionId,
        string action,
        uint? rowVersion,
        string? comment = null)
    {
        var query = new List<string>();
        if (rowVersion is not null)
        {
            query.Add(
                $"rowVersion={rowVersion.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            query.Add($"comment={Uri.EscapeDataString(comment)}");
        }

        string suffix = query.Count == 0 ? string.Empty : "?" + string.Join('&', query);
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
}

internal sealed class WorkflowTestApplicationFactory(TestDatabaseSettings database)
    : CynaraWebApplicationFactory(database);
