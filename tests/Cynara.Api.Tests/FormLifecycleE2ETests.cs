using System.Net;
using System.Net.Http.Json;

using Cynara.Application.Audit;
using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

[Trait("Category", "E2E")]
public sealed partial class FormLifecycleE2ETests : IDisposable
{
    public FormLifecycleE2ETests()
    {
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "stage1-e2e");
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    [Fact]
    public async Task Stage1_CompleteWorkflow_ComponentsFormsResponsesAuditAndRetire()
    {
        await CreateAndPublishComponentAsync(
            "patient-demographics",
            MinimalComponentClinicalSchema("patient-name", "patient.name"),
            MinimalComponentUiSchema("patient-name", "Patient name")).ConfigureAwait(false);

        string clinical = FormWithComponentRefAndVitals("intake-section", "section.intake");
        string rules = BpValidationRulesSchema();

        await CreateFormAsync("stage1-intake", "Stage 1 intake", clinical, ui: null, rules).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("stage1-intake").ConfigureAwait(false);
        FormVersionDto published = await SubmitAndPublishAsync("stage1-intake", draft.RowVersion).ConfigureAwait(false);
        Assert.Equal("1.0.0", published.Version);
        Assert.Equal("1.0.0", published.PublishedSchemaVersion);
        Assert.DoesNotContain("component-ref", published.ClinicalSchemaJson, StringComparison.Ordinal);

        FormResponseDto response = await CreateResponseAsync("stage1-intake", "1.0.0").ConfigureAwait(false);
        FormResponseDto draftToDelete = await CreateResponseAsync("stage1-intake", "1.0.0").ConfigureAwait(false);

        response = await UpdateResponseAsync(
            response.Id,
            /*lang=json,strict*/
            """
            {
              "patient.name": "Ada Lovelace",
              "vital.bp.systolic": 120,
              "vital.bp.diastolic": 80
            }
            """,
            response.RowVersion).ConfigureAwait(false);

        response = await CompleteResponseAsync(response.Id, response.RowVersion).ConfigureAwait(false);
        Assert.Equal("completed", response.Status);
        Assert.Equal(3u, response.RevisionNumber);

        FormResponseRevisionDto firstRevision = await GetResponseRevisionAsync(response.Id, 1).ConfigureAwait(false);
        Assert.Equal("{}", firstRevision.AnswersJson);

        List<AuditEventDto> responseAudit = await ListAuditEventsAsync("form-response", response.Id).ConfigureAwait(false);
        Assert.Contains(responseAudit, item => string.Equals(item.Action, "response.created", StringComparison.Ordinal));
        Assert.Contains(responseAudit, item => string.Equals(item.Action, "response.updated", StringComparison.Ordinal));
        Assert.Contains(responseAudit, item => string.Equals(item.Action, "response.completed", StringComparison.Ordinal));

        FormResponseDto softDeleted = await SoftDeleteResponseAsync(draftToDelete.Id, "Duplicate entry").ConfigureAwait(false);
        Assert.NotNull(softDeleted.DeletedAt);

        using HttpResponseMessage hiddenGet = await Client.GetAsync(new Uri($"/api/responses/{draftToDelete.Id}", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, hiddenGet.StatusCode);

        FormResponseDto auditVisible = await GetResponseAsync(draftToDelete.Id, includeDeleted: true).ConfigureAwait(false);
        Assert.NotNull(auditVisible.DeletedAt);

        List<AuditEventDto> deleteAuditEvents = await ListAuditEventsAsync("form-response", draftToDelete.Id).ConfigureAwait(false);
        AuditEventDto deleteAudit = Assert.Single(deleteAuditEvents, item => string.Equals(item.Action, "response.draft.deleted", StringComparison.Ordinal));
        Assert.Contains("Duplicate entry", deleteAudit.MetadataJson, StringComparison.Ordinal);

        FormResponseDto completedAfterDelete = await GetResponseAsync(response.Id).ConfigureAwait(false);
        Assert.Equal("completed", completedAfterDelete.Status);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            new Uri("/api/forms/stage1-intake/versions/1.0.0/retire", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto retiredVersion = await GetVersionAsync("stage1-intake", "1.0.0").ConfigureAwait(false);
        Assert.Equal("retired", retiredVersion.Status);
        Assert.Contains("patient-name", retiredVersion.ClinicalSchemaJson, StringComparison.Ordinal);

        using HttpResponseMessage blockedCreate = await Client.PostAsJsonAsync(
            "/api/forms/stage1-intake/versions/1.0.0/responses",
            new CreateFormResponseRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, blockedCreate.StatusCode);

        FormResponseDto historicalAfterRetire = await GetResponseAsync(response.Id).ConfigureAwait(false);
        Assert.Contains("Ada Lovelace", historicalAfterRetire.AnswersJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stage1_ReviewRejectAndRepublish_RestoresDraftForEditing()
    {
        await CreateFormAsync("review-intake", "Review intake", MinimalClinicalSchema("notes", "form.notes"), ui: null).ConfigureAwait(false);

        FormVersionDto draft = await GetEditableVersionAsync("review-intake").ConfigureAwait(false);
        FormVersionDto inReview = await SubmitForReviewAsync("review-intake", draft.RowVersion).ConfigureAwait(false);

        var rejectRequest = new RejectFormReviewRequest("Add blood pressure fields.", inReview.RowVersion);
        using HttpResponseMessage rejectResponse = await Client.PostAsJsonAsync(
            "/api/forms/review-intake/draft/reject-review",
            rejectRequest).ConfigureAwait(false);
        await AssertStatusAsync(rejectResponse, HttpStatusCode.OK).ConfigureAwait(false);

        FormVersionDto restoredDraft = (await rejectResponse.Content.ReadFromJsonAsync<FormVersionDto>().ConfigureAwait(false))!;
        Assert.Equal("draft", restoredDraft.Status);
        Assert.Equal("rejected", restoredDraft.LastReviewDecision);

        string updatedClinical = MinimalClinicalSchema("bp-notes", "form.bp-notes");
        restoredDraft = await UpdateDraftAsync(
            "review-intake",
            updatedClinical,
            restoredDraft.UiSchemaJson,
            restoredDraft.RulesSchemaJson,
            restoredDraft.RowVersion).ConfigureAwait(false);

        FormVersionDto published = await SubmitAndPublishAsync("review-intake", restoredDraft.RowVersion).ConfigureAwait(false);
        Assert.Equal("published", published.Status);
    }

    [Fact]
    public async Task Stage1_HistoricalResponse_RemainsOnPublishedVersionAfterNewerVersion()
    {
        await CreateFormAsync(
            "versioned-intake",
            "Versioned intake",
            MinimalClinicalSchema("patient-name-v1", "patient.name"),
ui: null).ConfigureAwait(false);

        FormVersionDto draftV1 = await GetEditableVersionAsync("versioned-intake").ConfigureAwait(false);
        FormVersionDto publishedV1 = await SubmitAndPublishAsync("versioned-intake", draftV1.RowVersion).ConfigureAwait(false);
        Guid publishedV1Id = publishedV1.Id;

        FormResponseDto historical = await CreateResponseAsync("versioned-intake", "1.0.0").ConfigureAwait(false);
        historical = await UpdateResponseAsync(
            historical.Id,
            /*lang=json,strict*/
            """{"patient.name":"Historical Ada"}""",
            historical.RowVersion).ConfigureAwait(false);
        historical = await CompleteResponseAsync(historical.Id, historical.RowVersion).ConfigureAwait(false);

        using HttpResponseMessage createDraftResponse = await Client.PostAsync(
            new Uri("/api/forms/versioned-intake/draft", UriKind.Relative),
            content: null).ConfigureAwait(false);
        await AssertStatusAsync(createDraftResponse, HttpStatusCode.Created).ConfigureAwait(false);

        FormVersionDto draftV2 = await GetEditableVersionAsync("versioned-intake").ConfigureAwait(false);
        string clinicalV2 = MinimalClinicalSchema("patient-name-v2", "patient.display-name");
        draftV2 = await UpdateDraftAsync("versioned-intake", clinicalV2, draftV2.UiSchemaJson, draftV2.RulesSchemaJson, draftV2.RowVersion).ConfigureAwait(false);
        FormVersionDto publishedV2 = await SubmitAndPublishAsync("versioned-intake", draftV2.RowVersion).ConfigureAwait(false);

        Assert.Equal("1.0.1", publishedV2.Version);
        Assert.NotEqual(publishedV1Id, publishedV2.Id);

        FormResponseDto rereadHistorical = await GetResponseAsync(historical.Id).ConfigureAwait(false);
        Assert.Equal(publishedV1Id, rereadHistorical.FormVersionId);
        Assert.Equal("1.0.0", rereadHistorical.FormVersion);
        Assert.Contains("Historical Ada", rereadHistorical.AnswersJson, StringComparison.Ordinal);

        FormVersionDto frozenSnapshot = await GetVersionAsync("versioned-intake", "1.0.0").ConfigureAwait(false);
        Assert.Contains("patient-name-v1", frozenSnapshot.ClinicalSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("patient-name-v2", frozenSnapshot.ClinicalSchemaJson, StringComparison.Ordinal);

        FormVersionDto currentSnapshot = await GetVersionAsync("versioned-intake", "1.0.1").ConfigureAwait(false);
        Assert.Contains("patient-name-v2", currentSnapshot.ClinicalSchemaJson, StringComparison.Ordinal);

        FormResponseDto newResponse = await CreateResponseAsync("versioned-intake", "1.0.1").ConfigureAwait(false);
        Assert.Equal("1.0.1", newResponse.FormVersion);
        Assert.NotEqual(historical.Id, newResponse.Id);
    }

    [Fact]
    public async Task Stage1_Guardrails_InvalidSchemaStaleRevisionAndInvalidComplete()
    {
        await CreateFormAsync("guardrails", "Guardrails", MinimalClinicalSchema("notes", "form.notes"), ui: null).ConfigureAwait(false);
        FormVersionDto draft = await GetEditableVersionAsync("guardrails").ConfigureAwait(false);

        var invalidSchemaUpdate = new UpdateFormDraftRequest("{}", draft.UiSchemaJson, draft.RulesSchemaJson, draft.RowVersion);
        using HttpResponseMessage invalidSchemaResponse = await Client.PutAsJsonAsync(
            "/api/forms/guardrails/draft",
            invalidSchemaUpdate).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSchemaResponse.StatusCode);

        FormVersionDto published = await SubmitAndPublishAsync("guardrails", draft.RowVersion).ConfigureAwait(false);
        FormResponseDto response = await CreateResponseAsync("guardrails", published.Version!).ConfigureAwait(false);

        var staleUpdate = new UpdateFormResponseRequest(/*lang=json,strict*/ """{"form.notes":"first"}""", response.RowVersion);
        using HttpResponseMessage firstUpdate = await Client.PutAsJsonAsync($"/api/responses/{response.Id}", staleUpdate).ConfigureAwait(false);
        await AssertStatusAsync(firstUpdate, HttpStatusCode.OK).ConfigureAwait(false);

        var conflictingUpdate = new UpdateFormResponseRequest(/*lang=json,strict*/ """{"form.notes":"second"}""", response.RowVersion);
        using HttpResponseMessage conflictResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{response.Id}",
            conflictingUpdate).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        await CreateFormAsync("bp-guardrails", "BP guardrails", BpClinicalSchema(), ui: null, BpValidationRulesSchema()).ConfigureAwait(false);
        FormVersionDto bpDraft = await GetEditableVersionAsync("bp-guardrails").ConfigureAwait(false);
        FormVersionDto bpPublished = await SubmitAndPublishAsync("bp-guardrails", bpDraft.RowVersion).ConfigureAwait(false);
        FormResponseDto bpResponse = await CreateResponseAsync("bp-guardrails", bpPublished.Version!).ConfigureAwait(false);
        bpResponse = await UpdateResponseAsync(
            bpResponse.Id,
            /*lang=json,strict*/
            """
            {
              "vital.bp.systolic": 120,
              "vital.bp.diastolic": 130
            }
            """,
            bpResponse.RowVersion).ConfigureAwait(false);

        using HttpResponseMessage invalidComplete = await Client.PostAsJsonAsync(
            $"/api/responses/{bpResponse.Id}/complete",
            new CompleteFormResponseRequest(bpResponse.RowVersion)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, invalidComplete.StatusCode);

        string body = await invalidComplete.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.Contains("BP_SYSTOLIC_GT_DIASTOLIC", body, StringComparison.Ordinal);
    }

    private HttpClient Client { get; }
}
