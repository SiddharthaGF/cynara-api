using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests;

[Collection(PostgresFixtureDefinition.Name)]
[Trait("Category", "E2E")]
public sealed partial class FormLifecycleE2ETests : IDisposable
{
    public FormLifecycleE2ETests(PostgreSqlDatabaseFixture database)
    {
        Factory = new FormWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.Add("X-Actor-Id", "stage1-e2e");
        Api = new JsonApiClient(Client);
        Api.UseHospitalContext(Factory.BootstrapOptions.BootstrapCode);
        Workflow = new JsonApiWorkflow(Api, Client);
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    [Fact]
    public async Task Stage1_CompleteWorkflow_ComponentsFormsResponsesAuditAndRetire()
    {
        string versionId = await PublishStage1IntakeFormAsync()
            .ConfigureAwait(false);

        using JsonDocument responseDoc = await Workflow.CreateResponseAsync(versionId)
            .ConfigureAwait(false);
        using JsonDocument draftToDeleteDoc = await Workflow.CreateResponseAsync(versionId)
            .ConfigureAwait(false);
        string responseId = JsonApiClient.RequireId(responseDoc);
        string draftToDeleteId = JsonApiClient.RequireId(draftToDeleteDoc);

        await CompleteStage1ResponseAndAssertAuditAsync(
            responseId,
            JsonApiClient.AttrUInt(responseDoc, "rowVersion"))
            .ConfigureAwait(false);
        await SoftDeleteStage1DraftAndAssertAsync(draftToDeleteId, responseId)
            .ConfigureAwait(false);
        await RetireStage1VersionAndAssertHistoryAsync(versionId, responseId)
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task Stage1_ReviewRejectAndRepublish_RestoresDraftForEditing()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "review-intake",
            "Review intake",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);

        using JsonDocument inReview = await Api.PostActionAsync(
            $"/api/formVersions/{draftId}/submit-review",
            new { rowVersion }).ConfigureAwait(false);

        using JsonDocument restoredDraft = await Api.PostActionAsync(
            $"/api/formVersions/{draftId}/reject-review",
            new
            {
                comment = "Add blood pressure fields.",
                rowVersion = JsonApiClient.AttrUInt(inReview, "rowVersion"),
            }).ConfigureAwait(false);
        Assert.Equal("draft", JsonApiClient.AttrString(restoredDraft, "status"));
        Assert.Equal(
            "rejected",
            JsonApiClient.AttrString(restoredDraft, "lastReviewDecision"));

        string updatedClinical = JsonApiWorkflow.MinimalClinicalSchema(
            "bp-notes",
            "form.bp-notes");
        using JsonDocument updated = await Api.PatchResourceAsync(
            "formVersions",
            draftId,
            new
            {
                clinicalSchemaJson = updatedClinical,
                uiSchemaJson = JsonApiClient.AttrString(restoredDraft, "uiSchemaJson"),
                rulesSchemaJson = JsonApiClient.AttrString(
                    restoredDraft,
                    "rulesSchemaJson"),
                rowVersion = JsonApiClient.AttrUInt(restoredDraft, "rowVersion"),
            }).ConfigureAwait(false);

        using JsonDocument published = await Workflow.SubmitAndPublishFormAsync(
            JsonApiClient.RequireId(updated)).ConfigureAwait(false);
        Assert.Equal("published", JsonApiClient.AttrString(published, "status"));
    }

    [Fact]
    public async Task Stage1_HistoricalResponse_RemainsOnPublishedVersionAfterNewerVersion()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "versioned-intake",
            "Versioned intake",
            JsonApiWorkflow.MinimalClinicalSchema("patient-name-v1", "patient.name"))
            .ConfigureAwait(false);
        string draftV1Id = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument publishedV1 = await Workflow.SubmitAndPublishFormAsync(draftV1Id)
            .ConfigureAwait(false);
        string publishedV1Id = JsonApiClient.RequireId(publishedV1);

        using JsonDocument historical = await Workflow.CreateResponseAsync(publishedV1Id)
            .ConfigureAwait(false);
        string historicalId = JsonApiClient.RequireId(historical);
        using JsonDocument historicalUpdated = await Api.PatchResourceAsync(
            "formResponses",
            historicalId,
            new
            {
                answersJson = /*lang=json,strict*/ """{"patient.name":"Historical Ada"}""",
                rowVersion = JsonApiClient.AttrUInt(historical, "rowVersion"),
            }).ConfigureAwait(false);
        using JsonDocument unusedDoc = await Api.PostActionAsync(
            $"/api/formResponses/{historicalId}/complete",
            new { rowVersion = JsonApiClient.AttrUInt(historicalUpdated, "rowVersion") })
            .ConfigureAwait(false);

        using HttpResponseMessage createDraftResponse = await Client.PostAsync(
            new Uri($"/api/formDefinitions/{definitionId}/create-draft", UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createDraftResponse.StatusCode);

        string draftV2Id = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument draftV2 = await Workflow.GetVersionAsync(
            "formVersions",
            draftV2Id).ConfigureAwait(false);
        string clinicalV2 = JsonApiWorkflow.MinimalClinicalSchema(
            "patient-name-v2",
            "patient.display-name");
        using JsonDocument updatedV2 = await Api.PatchResourceAsync(
            "formVersions",
            draftV2Id,
            new
            {
                clinicalSchemaJson = clinicalV2,
                uiSchemaJson = JsonApiClient.AttrString(draftV2, "uiSchemaJson"),
                rulesSchemaJson = JsonApiClient.AttrString(draftV2, "rulesSchemaJson"),
                rowVersion = JsonApiClient.AttrUInt(draftV2, "rowVersion"),
            }).ConfigureAwait(false);
        using JsonDocument publishedV2 = await Workflow.SubmitAndPublishFormAsync(
            JsonApiClient.RequireId(updatedV2)).ConfigureAwait(false);

        Assert.Equal("1.0.1", JsonApiClient.AttrString(publishedV2, "version"));
        Assert.NotEqual(publishedV1Id, JsonApiClient.RequireId(publishedV2));

        using JsonDocument rereadHistorical = await Api.GetAsync(
            $"/api/formResponses/{historicalId}?include=formVersion")
            .ConfigureAwait(false);
        Assert.Contains(
            "Historical Ada",
            JsonApiClient.AttrString(rereadHistorical, "answersJson"),
            StringComparison.Ordinal);
        string relatedVersionId = rereadHistorical.RootElement
            .GetProperty("data")
            .GetProperty("relationships")
            .GetProperty("formVersion")
            .GetProperty("data")
            .GetProperty("id")
            .GetString()!;
        Assert.Equal(publishedV1Id, relatedVersionId);

        using JsonDocument frozenSnapshot = await Api.GetAsync(
            $"/api/formVersions/{publishedV1Id}").ConfigureAwait(false);
        Assert.Contains(
            "patient-name-v1",
            JsonApiClient.AttrString(frozenSnapshot, "clinicalSchemaJson"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "patient-name-v2",
            JsonApiClient.AttrString(frozenSnapshot, "clinicalSchemaJson"),
            StringComparison.Ordinal);

        using JsonDocument currentSnapshot = await Api.GetAsync(
            $"/api/formVersions/{JsonApiClient.RequireId(publishedV2)}")
            .ConfigureAwait(false);
        Assert.Contains(
            "patient-name-v2",
            JsonApiClient.AttrString(currentSnapshot, "clinicalSchemaJson"),
            StringComparison.Ordinal);

        using JsonDocument newResponse = await Workflow.CreateResponseAsync(
            JsonApiClient.RequireId(publishedV2)).ConfigureAwait(false);
        Assert.NotEqual(historicalId, JsonApiClient.RequireId(newResponse));
    }

    [Fact]
    public async Task Stage1_Guardrails_InvalidSchemaStaleRevisionAndInvalidComplete()
    {
        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "guardrails",
            "Guardrails",
            JsonApiWorkflow.MinimalClinicalSchema("notes", "form.notes"))
            .ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        uint rowVersion = await Workflow.GetRowVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);

        using var invalidContent = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formVersions",
                id = draftId,
                attributes = new
                {
                    clinicalSchemaJson = "{}",
                    rowVersion,
                },
            },
        });
        using var invalidRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formVersions/{draftId}", UriKind.Relative))
        {
            Content = invalidContent,
        };
        using HttpResponseMessage invalidSchemaResponse = await Client
            .SendAsync(invalidRequest)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSchemaResponse.StatusCode);

        using JsonDocument published = await Workflow.SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);
        using JsonDocument response = await Workflow.CreateResponseAsync(
            JsonApiClient.RequireId(published)).ConfigureAwait(false);
        string responseId = JsonApiClient.RequireId(response);
        uint responseRowVersion = JsonApiClient.AttrUInt(response, "rowVersion");

        using JsonDocument unusedDoc = await Api.PatchResourceAsync(
            "formResponses",
            responseId,
            new
            {
                answersJson = /*lang=json,strict*/ """{"form.notes":"first"}""",
                rowVersion = responseRowVersion,
            }).ConfigureAwait(false);

        using var conflictContent = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formResponses",
                id = responseId,
                attributes = new
                {
                    answersJson = /*lang=json,strict*/ """{"form.notes":"second"}""",
                    rowVersion = responseRowVersion,
                },
            },
        });
        using var conflictRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formResponses/{responseId}", UriKind.Relative))
        {
            Content = conflictContent,
        };
        using HttpResponseMessage conflictResponse = await Client.SendAsync(conflictRequest)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        (string unusedDefId, string bpVersionId) = await Workflow.PublishFormAsync(
            "bp-guardrails",
            "BP guardrails",
            BpClinicalSchema(),
            uiSchemaJson: null,
            BpValidationRulesSchema()).ConfigureAwait(false);
        using JsonDocument bpResponse = await Workflow.CreateResponseAsync(bpVersionId)
            .ConfigureAwait(false);
        string bpResponseId = JsonApiClient.RequireId(bpResponse);
        using JsonDocument bpUpdated = await Api.PatchResourceAsync(
            "formResponses",
            bpResponseId,
            new
            {
                answersJson = /*lang=json,strict*/ """
                    {
                      "vital.bp.systolic": 120,
                      "vital.bp.diastolic": 130
                    }
                    """,
                rowVersion = JsonApiClient.AttrUInt(bpResponse, "rowVersion"),
            }).ConfigureAwait(false);

        using HttpResponseMessage invalidComplete = await Api.PostActionRawAsync(
            $"/api/formResponses/{bpResponseId}/complete",
            new { rowVersion = JsonApiClient.AttrUInt(bpUpdated, "rowVersion") })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, invalidComplete.StatusCode);
        string body = await invalidComplete.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("BP_SYSTOLIC_GT_DIASTOLIC", body, StringComparison.Ordinal);
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }
}
