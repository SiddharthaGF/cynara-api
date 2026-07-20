using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Forms;
using Cynara.Domain.Audit;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cynara.Api.Tests;

public class FormResponseLifecycleTests : IDisposable
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
    }

    [Fact]
    public async Task CreateResponse_AgainstPublishedForm_Succeeds()
    {
        await PublishFormAsync("intake", "Intake");

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/api/forms/intake/versions/1.0.0/responses",
            new CreateFormResponseRequest());
        await AssertStatusAsync(createResponse, HttpStatusCode.Created);

        FormResponseDto response = (await createResponse.Content.ReadFromJsonAsync<FormResponseDto>())!;
        Assert.Equal("intake", response.FormCode);
        Assert.Equal("1.0.0", response.FormVersion);
        Assert.Equal("draft", response.Status);
        Assert.Equal("{}", response.AnswersJson);
        Assert.Equal(1u, response.RevisionNumber);
        Assert.Equal(0u, response.RowVersion);
        Assert.Null(response.DeletedAt);

        await AssertAuditEventsRecordedAsync(response.Id, "response.created");
    }

    [Fact]
    public async Task CreateResponse_AgainstDraftOnlyForm_ReturnsNotFound()
    {
        var createFormRequest = new CreateFormRequest(
            "draft-only",
            "Draft only",
            MinimalClinicalSchema("notes", "form.notes"),
            null);

        using HttpResponseMessage createFormResponse = await Client.PostAsJsonAsync("/api/forms", createFormRequest);
        await AssertStatusAsync(createFormResponse, HttpStatusCode.Created);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/api/forms/draft-only/versions/1.0.0/responses",
            new CreateFormResponseRequest());
        await AssertStatusAsync(createResponse, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateResponse_IncrementsRevisionAndIsReconstructable()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("revision-test");

        var firstUpdate = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"revision-test.field":"Ada"}""",
            created.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            firstUpdate);
        await AssertStatusAsync(updateResponse, HttpStatusCode.OK);

        FormResponseDto updated = (await updateResponse.Content.ReadFromJsonAsync<FormResponseDto>())!;
        Assert.Equal(2u, updated.RevisionNumber);
        Assert.Equal(1u, updated.RowVersion);
        Assert.Contains("Ada", updated.AnswersJson, StringComparison.Ordinal);

        using HttpResponseMessage revisionsResponse = await Client.GetAsync($"/api/responses/{created.Id}/revisions");
        await AssertStatusAsync(revisionsResponse, HttpStatusCode.OK);

        List<FormResponseRevisionDto> revisions =
            (await revisionsResponse.Content.ReadFromJsonAsync<List<FormResponseRevisionDto>>())!;
        Assert.Equal(2, revisions.Count);
        Assert.Equal("{}", revisions[0].AnswersJson);
        Assert.Contains("Ada", revisions[1].AnswersJson, StringComparison.Ordinal);

        using HttpResponseMessage revisionOneResponse = await Client.GetAsync(
            $"/api/responses/{created.Id}/revisions/1");
        await AssertStatusAsync(revisionOneResponse, HttpStatusCode.OK);

        FormResponseRevisionDto revisionOne =
            (await revisionOneResponse.Content.ReadFromJsonAsync<FormResponseRevisionDto>())!;
        Assert.Equal("{}", revisionOne.AnswersJson);
        Assert.Equal("draft", revisionOne.Status);
    }

    [Fact]
    public async Task UpdateResponse_WithStaleRowVersion_ReturnsConflict()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("concurrency-test");

        var staleUpdate = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"concurrency-test.field":"first"}""",
            created.RowVersion);
        using HttpResponseMessage firstUpdate = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            staleUpdate);
        await AssertStatusAsync(firstUpdate, HttpStatusCode.OK);

        var conflictingUpdate = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"concurrency-test.field":"second"}""",
            created.RowVersion);
        using HttpResponseMessage conflictResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            conflictingUpdate);
        await AssertStatusAsync(conflictResponse, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CompleteResponse_LocksFurtherEdits()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("complete-test");

        var updateRequest = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"complete-test.field":"ready"}""",
            created.RowVersion);
        using HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            updateRequest);
        await AssertStatusAsync(updateResponse, HttpStatusCode.OK);

        FormResponseDto updated = (await updateResponse.Content.ReadFromJsonAsync<FormResponseDto>())!;

        var completeRequest = new CompleteFormResponseRequest(updated.RowVersion);
        using HttpResponseMessage completeResponse = await Client.PostAsJsonAsync(
            $"/api/responses/{created.Id}/complete",
            completeRequest);
        await AssertStatusAsync(completeResponse, HttpStatusCode.OK);

        FormResponseDto completed = (await completeResponse.Content.ReadFromJsonAsync<FormResponseDto>())!;
        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(3u, completed.RevisionNumber);

        var editAfterComplete = new UpdateFormResponseRequest(
                                 /*lang=json,strict*/
                                 """{"complete-test.field":"changed"}""",
            completed.RowVersion);
        using HttpResponseMessage editResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{created.Id}",
            editAfterComplete);
        await AssertStatusAsync(editResponse, HttpStatusCode.Conflict);

        await AssertAuditEventsRecordedAsync(
            created.Id,
            "response.created",
            "response.updated",
            "response.completed");
    }

    [Fact]
    public async Task SoftDeleteDraft_HidesFromNormalGetButVisibleForAudit()
    {
        FormResponseDto created = await CreatePublishedResponseAsync("soft-delete-test");

        using HttpResponseMessage deleteResponse = await Client.DeleteAsync(
            $"/api/responses/{created.Id}?reason=No%20longer%20needed");
        await AssertStatusAsync(deleteResponse, HttpStatusCode.NoContent);

        using HttpResponseMessage getResponse = await Client.GetAsync($"/api/responses/{created.Id}");
        await AssertStatusAsync(getResponse, HttpStatusCode.NotFound);

        using HttpResponseMessage auditGetResponse = await Client.GetAsync(
            $"/api/responses/{created.Id}?includeDeleted=true");
        await AssertStatusAsync(auditGetResponse, HttpStatusCode.OK);

        FormResponseDto deleted = (await auditGetResponse.Content.ReadFromJsonAsync<FormResponseDto>())!;
        Assert.NotNull(deleted.DeletedAt);

        using HttpResponseMessage revisionsResponse = await Client.GetAsync($"/api/responses/{created.Id}/revisions");
        await AssertStatusAsync(revisionsResponse, HttpStatusCode.OK);

        await AssertAuditEventsRecordedAsync(created.Id, "response.draft.deleted");
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private async Task PublishFormAsync(string code, string name)
    {
        var createFormRequest = new CreateFormRequest(
            code,
            name,
            MinimalClinicalSchema("field", $"{code}.field"),
            null);

        using HttpResponseMessage createFormResponse = await Client.PostAsJsonAsync("/api/forms", createFormRequest);
        await AssertStatusAsync(createFormResponse, HttpStatusCode.Created);

        FormVersionDto draft = await GetEditableVersionAsync(code);
        FormVersionDto inReview = await SubmitForReviewAsync(code, draft.RowVersion);
        using HttpResponseMessage publishResponse = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion));
        await AssertStatusAsync(publishResponse, HttpStatusCode.OK);
    }

    private async Task<FormVersionDto> SubmitForReviewAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/submit-review",
            new SubmitFormDraftForReviewRequest(rowVersion));
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>())!;
    }

    private async Task<FormResponseDto> CreatePublishedResponseAsync(string code)
    {
        await PublishFormAsync(code, code);

        using HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/versions/1.0.0/responses",
            new CreateFormResponseRequest());
        await AssertStatusAsync(createResponse, HttpStatusCode.Created);
        return (await createResponse.Content.ReadFromJsonAsync<FormResponseDto>())!;
    }

    private async Task<FormVersionDto> GetEditableVersionAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync($"/api/forms/{code}/draft");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>())!;
    }

    private async Task AssertAuditEventsRecordedAsync(Guid resourceId, params string[] actions)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        List<AuditEvent> events = [.. (await dbContext.AuditEvents
            .Where(item => item.ResourceId == resourceId)
            .ToListAsync())
            .OrderBy(item => item.OccurredAt)];

        foreach (string action in actions)
        {
            Assert.Contains(events, item => item.Action == action);
        }
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
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
