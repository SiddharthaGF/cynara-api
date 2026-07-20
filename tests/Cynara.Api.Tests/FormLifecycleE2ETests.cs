using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Cynara.Application.Audit;
using Cynara.Application.Components;
using Cynara.Application.Forms;

using Xunit;

namespace Cynara.Api.Tests;

[Trait("Category", "E2E")]
public sealed class FormLifecycleE2ETests : IDisposable
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
            MinimalComponentUiSchema("patient-name", "Patient name"));

        string clinical = FormWithComponentRefAndVitals("intake-section", "section.intake");
        string rules = BpValidationRulesSchema();

        await CreateFormAsync("stage1-intake", "Stage 1 intake", clinical, null, rules);

        FormVersionDto draft = await GetEditableVersionAsync("stage1-intake");
        FormVersionDto published = await SubmitAndPublishAsync("stage1-intake", draft.RowVersion);
        Assert.Equal("1.0.0", published.Version);
        Assert.Equal("1.0.0", published.PublishedSchemaVersion);
        Assert.DoesNotContain("component-ref", published.ClinicalSchemaJson, StringComparison.Ordinal);

        FormResponseDto response = await CreateResponseAsync("stage1-intake", "1.0.0");
        FormResponseDto draftToDelete = await CreateResponseAsync("stage1-intake", "1.0.0");

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
            response.RowVersion);

        response = await CompleteResponseAsync(response.Id, response.RowVersion);
        Assert.Equal("completed", response.Status);
        Assert.Equal(3u, response.RevisionNumber);

        FormResponseRevisionDto firstRevision = await GetResponseRevisionAsync(response.Id, 1);
        Assert.Equal("{}", firstRevision.AnswersJson);

        List<AuditEventDto> responseAudit = await ListAuditEventsAsync("form-response", response.Id);
        Assert.Contains(responseAudit, item => item.Action == "response.created");
        Assert.Contains(responseAudit, item => item.Action == "response.updated");
        Assert.Contains(responseAudit, item => item.Action == "response.completed");

        FormResponseDto softDeleted = await SoftDeleteResponseAsync(draftToDelete.Id, "Duplicate entry");
        Assert.NotNull(softDeleted.DeletedAt);

        using HttpResponseMessage hiddenGet = await Client.GetAsync($"/api/responses/{draftToDelete.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenGet.StatusCode);

        FormResponseDto auditVisible = await GetResponseAsync(draftToDelete.Id, includeDeleted: true);
        Assert.NotNull(auditVisible.DeletedAt);

        List<AuditEventDto> deleteAuditEvents = await ListAuditEventsAsync("form-response", draftToDelete.Id);
        AuditEventDto deleteAudit = Assert.Single(deleteAuditEvents, item => item.Action == "response.draft.deleted");
        Assert.Contains("Duplicate entry", deleteAudit.MetadataJson, StringComparison.Ordinal);

        FormResponseDto completedAfterDelete = await GetResponseAsync(response.Id);
        Assert.Equal("completed", completedAfterDelete.Status);

        using HttpResponseMessage retireResponse = await Client.PostAsync(
            "/api/forms/stage1-intake/versions/1.0.0/retire",
            content: null);
        await AssertStatusAsync(retireResponse, HttpStatusCode.OK);

        FormVersionDto retiredVersion = await GetVersionAsync("stage1-intake", "1.0.0");
        Assert.Equal("retired", retiredVersion.Status);
        Assert.Contains("patient-name", retiredVersion.ClinicalSchemaJson, StringComparison.Ordinal);

        using HttpResponseMessage blockedCreate = await Client.PostAsJsonAsync(
            "/api/forms/stage1-intake/versions/1.0.0/responses",
            new CreateFormResponseRequest());
        Assert.Equal(HttpStatusCode.NotFound, blockedCreate.StatusCode);

        FormResponseDto historicalAfterRetire = await GetResponseAsync(response.Id);
        Assert.Contains("Ada Lovelace", historicalAfterRetire.AnswersJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stage1_ReviewRejectAndRepublish_RestoresDraftForEditing()
    {
        await CreateFormAsync("review-intake", "Review intake", MinimalClinicalSchema("notes", "form.notes"), null);

        FormVersionDto draft = await GetEditableVersionAsync("review-intake");
        FormVersionDto inReview = await SubmitForReviewAsync("review-intake", draft.RowVersion);

        var rejectRequest = new RejectFormReviewRequest("Add blood pressure fields.", inReview.RowVersion);
        using HttpResponseMessage rejectResponse = await Client.PostAsJsonAsync(
            "/api/forms/review-intake/draft/reject-review",
            rejectRequest);
        await AssertStatusAsync(rejectResponse, HttpStatusCode.OK);

        FormVersionDto restoredDraft = (await rejectResponse.Content.ReadFromJsonAsync<FormVersionDto>())!;
        Assert.Equal("draft", restoredDraft.Status);
        Assert.Equal("rejected", restoredDraft.LastReviewDecision);

        string updatedClinical = MinimalClinicalSchema("bp-notes", "form.bp-notes");
        restoredDraft = await UpdateDraftAsync(
            "review-intake",
            updatedClinical,
            restoredDraft.UiSchemaJson,
            restoredDraft.RulesSchemaJson,
            restoredDraft.RowVersion);

        FormVersionDto published = await SubmitAndPublishAsync("review-intake", restoredDraft.RowVersion);
        Assert.Equal("published", published.Status);
    }

    [Fact]
    public async Task Stage1_HistoricalResponse_RemainsOnPublishedVersionAfterNewerVersion()
    {
        await CreateFormAsync(
            "versioned-intake",
            "Versioned intake",
            MinimalClinicalSchema("patient-name-v1", "patient.name"),
            null);

        FormVersionDto draftV1 = await GetEditableVersionAsync("versioned-intake");
        FormVersionDto publishedV1 = await SubmitAndPublishAsync("versioned-intake", draftV1.RowVersion);
        Guid publishedV1Id = publishedV1.Id;

        FormResponseDto historical = await CreateResponseAsync("versioned-intake", "1.0.0");
        historical = await UpdateResponseAsync(
            historical.Id,
                                 /*lang=json,strict*/
                                 """{"patient.name":"Historical Ada"}""",
            historical.RowVersion);
        historical = await CompleteResponseAsync(historical.Id, historical.RowVersion);

        using HttpResponseMessage createDraftResponse = await Client.PostAsync(
            "/api/forms/versioned-intake/draft",
            content: null);
        await AssertStatusAsync(createDraftResponse, HttpStatusCode.Created);

        FormVersionDto draftV2 = await GetEditableVersionAsync("versioned-intake");
        string clinicalV2 = MinimalClinicalSchema("patient-name-v2", "patient.display-name");
        draftV2 = await UpdateDraftAsync("versioned-intake", clinicalV2, draftV2.UiSchemaJson, draftV2.RulesSchemaJson, draftV2.RowVersion);
        FormVersionDto publishedV2 = await SubmitAndPublishAsync("versioned-intake", draftV2.RowVersion);

        Assert.Equal("1.0.1", publishedV2.Version);
        Assert.NotEqual(publishedV1Id, publishedV2.Id);

        FormResponseDto rereadHistorical = await GetResponseAsync(historical.Id);
        Assert.Equal(publishedV1Id, rereadHistorical.FormVersionId);
        Assert.Equal("1.0.0", rereadHistorical.FormVersion);
        Assert.Contains("Historical Ada", rereadHistorical.AnswersJson, StringComparison.Ordinal);

        FormVersionDto frozenSnapshot = await GetVersionAsync("versioned-intake", "1.0.0");
        Assert.Contains("patient-name-v1", frozenSnapshot.ClinicalSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("patient-name-v2", frozenSnapshot.ClinicalSchemaJson, StringComparison.Ordinal);

        FormVersionDto currentSnapshot = await GetVersionAsync("versioned-intake", "1.0.1");
        Assert.Contains("patient-name-v2", currentSnapshot.ClinicalSchemaJson, StringComparison.Ordinal);

        FormResponseDto newResponse = await CreateResponseAsync("versioned-intake", "1.0.1");
        Assert.Equal("1.0.1", newResponse.FormVersion);
        Assert.NotEqual(historical.Id, newResponse.Id);
    }

    [Fact]
    public async Task Stage1_Guardrails_InvalidSchemaStaleRevisionAndInvalidComplete()
    {
        await CreateFormAsync("guardrails", "Guardrails", MinimalClinicalSchema("notes", "form.notes"), null);
        FormVersionDto draft = await GetEditableVersionAsync("guardrails");

        var invalidSchemaUpdate = new UpdateFormDraftRequest("{}", draft.UiSchemaJson, draft.RulesSchemaJson, draft.RowVersion);
        using HttpResponseMessage invalidSchemaResponse = await Client.PutAsJsonAsync(
            "/api/forms/guardrails/draft",
            invalidSchemaUpdate);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSchemaResponse.StatusCode);

        FormVersionDto published = await SubmitAndPublishAsync("guardrails", draft.RowVersion);
        FormResponseDto response = await CreateResponseAsync("guardrails", published.Version!);

        var staleUpdate = new UpdateFormResponseRequest(/*lang=json,strict*/ """{"form.notes":"first"}""", response.RowVersion);
        using HttpResponseMessage firstUpdate = await Client.PutAsJsonAsync($"/api/responses/{response.Id}", staleUpdate);
        await AssertStatusAsync(firstUpdate, HttpStatusCode.OK);

        var conflictingUpdate = new UpdateFormResponseRequest(/*lang=json,strict*/ """{"form.notes":"second"}""", response.RowVersion);
        using HttpResponseMessage conflictResponse = await Client.PutAsJsonAsync(
            $"/api/responses/{response.Id}",
            conflictingUpdate);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        await CreateFormAsync("bp-guardrails", "BP guardrails", BpClinicalSchema(), null, BpValidationRulesSchema());
        FormVersionDto bpDraft = await GetEditableVersionAsync("bp-guardrails");
        FormVersionDto bpPublished = await SubmitAndPublishAsync("bp-guardrails", bpDraft.RowVersion);
        FormResponseDto bpResponse = await CreateResponseAsync("bp-guardrails", bpPublished.Version!);
        bpResponse = await UpdateResponseAsync(
            bpResponse.Id,
                                 /*lang=json,strict*/
                                 """
            {
              "vital.bp.systolic": 120,
              "vital.bp.diastolic": 130
            }
            """,
            bpResponse.RowVersion);

        using HttpResponseMessage invalidComplete = await Client.PostAsJsonAsync(
            $"/api/responses/{bpResponse.Id}/complete",
            new CompleteFormResponseRequest(bpResponse.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, invalidComplete.StatusCode);

        string body = await invalidComplete.Content.ReadAsStringAsync();
        Assert.Contains("BP_SYSTOLIC_GT_DIASTOLIC", body, StringComparison.Ordinal);
    }

    private HttpClient Client { get; }

    private FormWebApplicationFactory Factory { get; } = new();

    private async Task CreateAndPublishComponentAsync(string code, string clinical, string ui)
    {
        await CreateComponentAsync(code, code, clinical, ui);
        ComponentVersionDto draft = await GetComponentDraftAsync(code);
        await PublishComponentDraftAsync(code, draft.RowVersion);
    }

    private async Task CreateComponentAsync(string code, string name, string clinical, string ui)
    {
        var request = new CreateComponentRequest(code, name, clinical, ui);
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/components", request);
        await AssertStatusAsync(response, HttpStatusCode.Created);
    }

    private async Task CreateFormAsync(
        string code,
        string name,
        string clinical,
        string? ui,
        string? rules = null)
    {
        var request = new CreateFormRequest(code, name, clinical, ui, rules);
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/forms", request);
        await AssertStatusAsync(response, HttpStatusCode.Created);
    }

    private async Task<FormVersionDto> UpdateDraftAsync(
        string code,
        string clinical,
        string? ui,
        string? rules,
        uint rowVersion)
    {
        var request = new UpdateFormDraftRequest(clinical, ui, rules, rowVersion);
        using HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/forms/{code}/draft", request);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>())!;
    }

    private async Task<FormVersionDto> GetEditableVersionAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync($"/api/forms/{code}/draft");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>())!;
    }

    private async Task<FormVersionDto> GetVersionAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.GetAsync($"/api/forms/{code}/versions/{version}");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>())!;
    }

    private async Task<FormVersionDto> SubmitForReviewAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/submit-review",
            new SubmitFormDraftForReviewRequest(rowVersion));
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>())!;
    }

    private async Task<FormVersionDto> SubmitAndPublishAsync(string code, uint draftRowVersion)
    {
        FormVersionDto inReview = await SubmitForReviewAsync(code, draftRowVersion);
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/draft/publish",
            new PublishFormDraftRequest(inReview.RowVersion));
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormVersionDto>())!;
    }

    private async Task<FormResponseDto> CreateResponseAsync(string code, string version)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/forms/{code}/versions/{version}/responses",
            new CreateFormResponseRequest());
        await AssertStatusAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>())!;
    }

    private async Task<FormResponseDto> UpdateResponseAsync(Guid id, string answersJson, uint rowVersion)
    {
        var request = new UpdateFormResponseRequest(answersJson, rowVersion);
        using HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/responses/{id}", request);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>())!;
    }

    private async Task<FormResponseDto> CompleteResponseAsync(Guid id, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/responses/{id}/complete",
            new CompleteFormResponseRequest(rowVersion));
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>())!;
    }

    private async Task<FormResponseDto> GetResponseAsync(Guid id, bool includeDeleted = false)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            $"/api/responses/{id}?includeDeleted={includeDeleted.ToString().ToLowerInvariant()}");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormResponseDto>())!;
    }

    private async Task<FormResponseDto> SoftDeleteResponseAsync(Guid id, string reason)
    {
        using HttpResponseMessage response = await Client.DeleteAsync(
            $"/api/responses/{id}?reason={Uri.EscapeDataString(reason)}");
        await AssertStatusAsync(response, HttpStatusCode.NoContent);
        return await GetResponseAsync(id, includeDeleted: true);
    }

    private async Task<FormResponseRevisionDto> GetResponseRevisionAsync(Guid id, uint revisionNumber)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            $"/api/responses/{id}/revisions/{revisionNumber}");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FormResponseRevisionDto>())!;
    }

    private async Task<List<AuditEventDto>> ListAuditEventsAsync(string resourceType, Guid resourceId)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            $"/api/audit/events?resourceType={resourceType}&resourceId={resourceId}");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<AuditEventDto>>())!;
    }

    private async Task<ComponentVersionDto> GetComponentDraftAsync(string code)
    {
        using HttpResponseMessage response = await Client.GetAsync($"/api/components/{code}/draft");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ComponentVersionDto>())!;
    }

    private async Task PublishComponentDraftAsync(string code, uint rowVersion)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/components/{code}/draft/publish",
            new PublishComponentDraftRequest(rowVersion));
        await AssertStatusAsync(response, HttpStatusCode.OK);
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

    private static string MinimalClinicalSchema(string id, string code)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new[]
            {
                new
                {
                    id,
                    code,
                    type = "text",
                    maxLength = 500,
                },
            },
        });
    }

    private static string MinimalComponentClinicalSchema(string id, string code)
    {
        return MinimalClinicalSchema(id, code);
    }

    private static string MinimalComponentUiSchema(string fieldId, string label)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            clinicalSchemaVersion = "1.0.0",
            fields = new Dictionary<string, object>
            {
                [fieldId] = new
                {
                    label,
                    widget = "text-input",
                },
            },
        });
    }

    private static string BpClinicalSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                { "id": "systolic", "code": "vital.bp.systolic", "type": "integer" },
                { "id": "diastolic", "code": "vital.bp.diastolic", "type": "integer" }
              ]
            }
            """;
    }

    private static string BpValidationRulesSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {},
              "validations": [
                {
                  "code": "BP_SYSTOLIC_GT_DIASTOLIC",
                  "message": "Systolic must be greater than diastolic",
                  "when": {
                    "op": "and",
                    "args": [
                      { "op": "not", "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.systolic" }] }] },
                      { "op": "not", "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.diastolic" }] }] }
                    ]
                  },
                  "assert": {
                    "op": "gt",
                    "args": [
                      { "ref": "vital.bp.systolic" },
                      { "ref": "vital.bp.diastolic" }
                    ]
                  }
                }
              ]
            }
            """;
    }

    private static string FormWithComponentRefAndVitals(string sectionId, string sectionCode)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0.0",
            fields = new object[]
            {
                new
                {
                    id = sectionId,
                    code = sectionCode,
                    type = "component-ref",
                    componentCode = "patient-demographics",
                    componentVersion = "1.0.0",
                },
                new
                {
                    id = "systolic",
                    code = "vital.bp.systolic",
                    type = "integer",
                },
                new
                {
                    id = "diastolic",
                    code = "vital.bp.diastolic",
                    type = "integer",
                },
            },
        });
    }
}
