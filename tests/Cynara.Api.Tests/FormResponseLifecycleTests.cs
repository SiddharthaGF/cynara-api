using System.Net;
using System.Text.Json;

using Cynara.Domain.Audit;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class FormResponseLifecycleTests : IDisposable
{
    public FormResponseLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new FormWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "test-clinician");
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(Factory.BootstrapOptions.BootstrapCode);
        Workflow = new JsonApiWorkflow(Api, Client);
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateResponse_AgainstPublishedForm_Succeeds()
    {
        (_, string versionId) = await Workflow.PublishFormAsync(
            "intake",
            "Intake",
            JsonApiWorkflow.MinimalClinicalSchema("field", "intake.field"))
            .ConfigureAwait(false);

        using JsonDocument response = await Workflow.CreateResponseAsync(versionId)
            .ConfigureAwait(false);
        Assert.Equal("draft", JsonApiClient.AttrString(response, "status"));
        Assert.Equal("{}", JsonApiClient.AttrString(response, "answersJson"));
        Assert.Equal(1u, JsonApiClient.AttrUInt(response, "revisionNumber"));
        Assert.Equal(0u, JsonApiClient.AttrUInt(response, "rowVersion"));
        Assert.True(
            response.RootElement.GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("deletedAt")
                .ValueKind is JsonValueKind.Null);

        await AssertAuditEventsRecordedAsync(
            Guid.Parse(JsonApiClient.RequireId(response)),
            "response.created").ConfigureAwait(false);
    }

    [Fact]
    public async Task CreateResponse_AgainstDraftOnlyForm_ReturnsNotFound()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "draft-only",
            "Draft only",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);

        using var content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formResponses",
                attributes = new { answersJson = "{}" },
                relationships = new
                {
                    formVersion = new
                    {
                        data = new { type = "formVersions", id = draftId },
                    },
                },
            },
        });
        using HttpResponseMessage createResponse = await Client.PostAsync(
            new Uri("/api/formResponses", UriKind.Relative),
            content).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, createResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateResponse_IncrementsRevisionAndIsReconstructable()
    {
        using JsonDocument created = await CreatePublishedResponseAsync("revision-test")
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(created);

        using JsonDocument updated = await Api.PatchResourceAsync(
            "formResponses",
            id,
            new
            {
                answersJson = /*lang=json,strict*/ """{"revision-test.field":"Ada"}""",
                rowVersion = JsonApiClient.AttrUInt(created, "rowVersion"),
            }).ConfigureAwait(false);
        Assert.Equal(2u, JsonApiClient.AttrUInt(updated, "revisionNumber"));
        Assert.Equal(1u, JsonApiClient.AttrUInt(updated, "rowVersion"));
        Assert.Contains(
            "Ada",
            JsonApiClient.AttrString(updated, "answersJson"),
            StringComparison.Ordinal);

        using JsonDocument revisions = await Api.GetAsync(
            $"/api/formResponseRevisions?filter=equals(formResponse.id,'{id}')&sort=revisionNumber")
            .ConfigureAwait(false);
        JsonElement data = revisions.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal(
            "{}",
            data[0].GetProperty("attributes").GetProperty("answersJson").GetString());
        Assert.Contains(
            "Ada",
            data[1].GetProperty("attributes").GetProperty("answersJson").GetString(),
            StringComparison.Ordinal);

        string revisionOneId = data[0].GetProperty("id").GetString()!;
        using JsonDocument revisionOne = await Api.GetAsync(
            $"/api/formResponseRevisions/{revisionOneId}").ConfigureAwait(false);
        Assert.Equal("{}", JsonApiClient.AttrString(revisionOne, "answersJson"));
        Assert.Equal("draft", JsonApiClient.AttrString(revisionOne, "status"));
    }

    [Fact]
    public async Task UpdateResponse_WithStaleRowVersion_ReturnsConflict()
    {
        using JsonDocument created = await CreatePublishedResponseAsync("concurrency-test")
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(created);
        uint stale = JsonApiClient.AttrUInt(created, "rowVersion");

        using JsonDocument unusedDoc = await Api.PatchResourceAsync(
            "formResponses",
            id,
            new
            {
                answersJson = /*lang=json,strict*/ """{"concurrency-test.field":"first"}""",
                rowVersion = stale,
            }).ConfigureAwait(false);

        using var content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formResponses",
                id,
                attributes = new
                {
                    answersJson = /*lang=json,strict*/
                        """{"concurrency-test.field":"second"}""",
                    rowVersion = stale,
                },
            },
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formResponses/{id}", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage conflict = await Client.SendAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task CompleteResponse_LocksFurtherEdits()
    {
        using JsonDocument created = await CreatePublishedResponseAsync("complete-test")
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(created);

        using JsonDocument updated = await Api.PatchResourceAsync(
            "formResponses",
            id,
            new
            {
                answersJson = /*lang=json,strict*/ """{"complete-test.field":"ready"}""",
                rowVersion = JsonApiClient.AttrUInt(created, "rowVersion"),
            }).ConfigureAwait(false);

        using JsonDocument completed = await Api.PostActionAsync(
            $"/api/formResponses/{id}/complete",
            new { rowVersion = JsonApiClient.AttrUInt(updated, "rowVersion") })
            .ConfigureAwait(false);
        Assert.Equal("completed", JsonApiClient.AttrString(completed, "status"));
        Assert.False(string.IsNullOrWhiteSpace(
            JsonApiClient.AttrString(completed, "completedAt")));
        Assert.Equal(3u, JsonApiClient.AttrUInt(completed, "revisionNumber"));

        using var content = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formResponses",
                id,
                attributes = new
                {
                    answersJson = /*lang=json,strict*/
                        """{"complete-test.field":"changed"}""",
                    rowVersion = JsonApiClient.AttrUInt(completed, "rowVersion"),
                },
            },
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formResponses/{id}", UriKind.Relative))
        {
            Content = content,
        };
        using HttpResponseMessage editResponse = await Client.SendAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);

        await AssertAuditEventsRecordedAsync(
            Guid.Parse(id),
            "response.created",
            "response.updated",
            "response.completed").ConfigureAwait(false);
    }

    [Fact]
    public async Task SoftDeleteDraft_HidesFromNormalGetButVisibleForAudit()
    {
        using JsonDocument created = await CreatePublishedResponseAsync("soft-delete-test")
            .ConfigureAwait(false);
        string id = JsonApiClient.RequireId(created);

        using HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/formResponses/{id}?reason=No%20longer%20needed")
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage getResponse = await Api.SendGetAsync(
            $"/api/formResponses/{id}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        Domain.Forms.FormResponse? deleted = await dbContext.FormResponses
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == Guid.Parse(id))
            .ConfigureAwait(false);
        Assert.NotNull(deleted);
        Assert.NotNull(deleted.DeletedAt);

        await AssertAuditEventsRecordedAsync(Guid.Parse(id), "response.draft.deleted")
            .ConfigureAwait(false);
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private FormWebApplicationFactory Factory { get; }

    private async Task<JsonDocument> CreatePublishedResponseAsync(string code)
    {
        (_, string versionId) = await Workflow.PublishFormAsync(
            code,
            code,
            JsonApiWorkflow.MinimalClinicalSchema("field", $"{code}.field"))
            .ConfigureAwait(false);
        return await Workflow.CreateResponseAsync(versionId).ConfigureAwait(false);
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
