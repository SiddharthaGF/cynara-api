using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Forms;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cynara.Api.Tests;

public sealed class FormResponseLifecycleTests : IDisposable
{
    public FormResponseLifecycleTests()
    {
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "test-clinician");
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
        await PublishFormAsync("intake", "Intake").ConfigureAwait(false);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/api/forms/intake/versions/1.0.0/responses",
            new CreateFormResponseRequest()).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);

        FormResponseDto response = (await createResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
        Assert.Equal("intake", response.FormCode);
        Assert.Equal("1.0.0", response.FormVersion);
        Assert.Equal("draft", response.Status);
        Assert.Equal("{}", response.AnswersJson);
        Assert.Equal(1u, response.RevisionNumber);
        Assert.Equal(0u, response.RowVersion);
        Assert.Null(response.DeletedAt);

        await AssertAuditEventsRecordedAsync(response.Id, "response.created").ConfigureAwait(false);
    }

    [Fact]
    public async Task CreateResponse_AgainstDraftOnlyForm_ReturnsNotFound()
    {
        var createFormRequest = new CreateFormRequest(
            "draft-only",
            "Draft only",
            MinimalClinicalSchema("notes", "form.notes"),
UiSchemaJson: null);

        using HttpResponseMessage createFormResponse = await Client.PostAsJsonAsync("/api/forms", createFormRequest).ConfigureAwait(false);
        await AssertStatusAsync(createFormResponse, HttpStatusCode.Created).ConfigureAwait(false);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/api/forms/draft-only/versions/1.0.0/responses",
            new CreateFormResponseRequest()).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.NotFound).ConfigureAwait(false);
    }

    [Fact]
    public async Task UpdateResponse_IncrementsRevisionAndIsReconstructable()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("revision-test").ConfigureAwait(false);

        var firstUpdate = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"revision-test.field":"Ada"}""",
                                 created.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            firstUpdate).ConfigureAwait(false);
        await AssertStatusAsync(updateResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormResponseDto updated = (await updateResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
        Assert.Equal(2u, updated.RevisionNumber);
        Assert.Equal(1u, updated.RowVersion);
        Assert.Contains("Ada", updated.AnswersJson, StringComparison.Ordinal);

        using HttpResponseMessage revisionsResponse = await Client.GetAsync(new Uri($"/api/responses/{created.Id}/revisions", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(revisionsResponse, HttpStatusCode.OK).ConfigureAwait(false);

        List<FormResponseRevisionDto> revisions =
            (await revisionsResponse.Content.ReadFromJsonAsync<List<FormResponseRevisionDto>>().ConfigureAwait(false))!;
        Assert.Equal(2, revisions.Count);
        Assert.Equal("{}", revisions[0].AnswersJson);
        Assert.Contains("Ada", revisions[1].AnswersJson, StringComparison.Ordinal);

        using HttpResponseMessage revisionOneResponse = await Client.GetAsync(
            new Uri($"/api/responses/{created.Id}/revisions/1", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(revisionOneResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormResponseRevisionDto revisionOne =
            (await revisionOneResponse.Content.ReadFromJsonAsync<FormResponseRevisionDto>().ConfigureAwait(false))!;
        Assert.Equal("{}", revisionOne.AnswersJson);
        Assert.Equal("draft", revisionOne.Status);
    }

    [Fact]
    public async Task UpdateResponse_WithStaleRowVersion_ReturnsConflict()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("concurrency-test").ConfigureAwait(false);

        var staleUpdate = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"concurrency-test.field":"first"}""",
                                 created.RowVersion);
        using HttpResponseMessage firstUpdate = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            staleUpdate).ConfigureAwait(false);
        await AssertStatusAsync(firstUpdate, HttpStatusCode.OK).ConfigureAwait(false);

        var conflictingUpdate = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"concurrency-test.field":"second"}""",
                                 created.RowVersion);
        using HttpResponseMessage conflictResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            conflictingUpdate).ConfigureAwait(false);
        await AssertStatusAsync(conflictResponse, HttpStatusCode.Conflict).ConfigureAwait(false);
    }

    [Fact]
    public async Task CompleteResponse_LocksFurtherEdits()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("complete-test").ConfigureAwait(false);

        var updateRequest = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"complete-test.field":"ready"}""",
                                 created.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            updateRequest).ConfigureAwait(false);
        await AssertStatusAsync(updateResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormResponseDto updated = (await updateResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;

        var completeRequest = new CompleteFormResponseRequest(updated.RowVersion);
        using HttpResponseMessage completeResponse = await Client.PostAsJsonAsync(
            $"/api/responses/{created.Id}/complete",
            completeRequest).ConfigureAwait(false);
        await AssertStatusAsync(completeResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormResponseDto completed = (await completeResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(3u, completed.RevisionNumber);

        var editAfterComplete = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"complete-test.field":"changed"}""",
                                 completed.RowVersion);
        using HttpResponseMessage editResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            editAfterComplete).ConfigureAwait(false);
        await AssertStatusAsync(editResponse, HttpStatusCode.Conflict).ConfigureAwait(false);

        await AssertAuditEventsRecordedAsync(
            created.Id,
            "response.created",
            "response.updated",
            "response.completed").ConfigureAwait(false);
    }

    [Fact]
    public async Task SoftDeleteDraft_HidesFromNormalGetButVisibleForAudit()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("soft-delete-test").ConfigureAwait(false);

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync(
            new Uri($"/api/responses/{created.Id}?reason=No%20longer%20needed", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(deleteResponse, HttpStatusCode.NoContent).ConfigureAwait(false);

        using HttpResponseMessage getResponse = await Client.GetAsync(new Uri($"/api/responses/{created.Id}", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(getResponse, HttpStatusCode.NotFound).ConfigureAwait(false);

        using HttpResponseMessage auditGetResponse = await Client.GetAsync(
            new Uri($"/api/responses/{created.Id}?includeDeleted=true", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(auditGetResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormResponseDto deleted = (await auditGetResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
        Assert.NotNull(deleted.DeletedAt);

        using HttpResponseMessage revisionsResponse = await Client.GetAsync(new Uri($"/api/responses/{created.Id}/revisions", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(revisionsResponse, HttpStatusCode.OK).ConfigureAwait(false);

        await AssertAuditEventsRecordedAsync(created.Id, "response.draft.deleted").ConfigureAwait(false);
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private async Task PublishFormAsync(string code, string name)
    {
        var createFormRequest = new CreateFormRequest(
            code,
            name,
            MinimalClinicalSchema("field", $"{code}.field"),
UiSchemaJson: null);

        using HttpResponseMessage createFormResponse = await Client.PostAsJsonAsync("/api/forms", createFormRequest).ConfigureAwait(false);
        await AssertStatusAsync(createFormResponse, HttpStatusCode.Created).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync(code).ConfigureAwait(false);
        FormVersionDto inReview = await SubmitForReviewAsync(code, draft.RowVersion).ConfigureAwait(false);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK).ConfigureAwait(false);
    }

    private async Task<FormVersionDto> SubmitForReviewAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/submit-review",
            new SubmitFormDraftForReviewRequest(rowVersion)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task<FormResponseDto> CreatePublishedResponseAsync(string code)
    {
        await PublishFormAsync(code, code).ConfigureAwait(false);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/versions/1.0.0/responses",
            new CreateFormResponseRequest()).ConfigureAwait(false);
        await AssertStatusAsync(createResponse, HttpStatusCode.Created).ConfigureAwait(false);
        return (await createResponse.Content.ReadFromJsonAsync<FormResponseDto>().ConfigureAwait(false))!;
    }

    private async Task<FormVersionDto> GetEditableVersionAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri($"/api/forms/{code}/draft", UriKind.Relative)).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
    }

    private async Task AssertAuditEventsRecordedAsync(Guid resourceId, params string[] actions)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        List<AuditEvent> events = [.. (await dbContext.AuditEvents
            .Where(item => item.ResourceId == resourceId)
            .ToListAsync().ConfigureAwait(false))
            .OrderBy(item => item.OccurredAt)];

        foreach (string action in actions)
        {
            Assert.Contains(events, item => string.Equals(item.Action, action, StringComparison.Ordinal));
        }
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}"));
    }

    private static string MinimalClinicalSchema(string id, string fieldCode)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new[]
            {
                new
                {
                    id,
                    code = fieldCode,
                    type = "text",
                    maxLength = 500,
                },
            },
        });
    }
}
