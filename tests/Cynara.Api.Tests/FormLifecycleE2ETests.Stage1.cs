using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests;

public sealed partial class FormLifecycleE2ETests
{
    private async Task<string> PublishStage1IntakeFormAsync()
    {
        await CreateAndPublishComponentAsync(
            "patient-demographics",
            JsonApiWorkflow.MinimalClinicalSchema("patient-name", "patient.name"),
            JsonApiWorkflow.MinimalUiSchema("patient-name", "Patient name"))
            .ConfigureAwait(false);

        string clinical = FormWithComponentRefAndVitals("intake-section", "section.intake");
        string rules = BpValidationRulesSchema();

        string definitionId = await Workflow.CreateFormDefinitionAsync(
            "stage1-intake",
            "Stage 1 intake",
            clinical,
            uiSchemaJson: null,
            rules).ConfigureAwait(false);
        string draftId = await Workflow.GetFormDraftIdAsync(definitionId)
            .ConfigureAwait(false);
        using JsonDocument published = await Workflow.SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);
        string versionId = JsonApiClient.RequireId(published);
        Assert.Equal("1.0.0", JsonApiClient.AttrString(published, "version"));
        Assert.Equal(
            "1.0.0",
            JsonApiClient.AttrString(published, "publishedSchemaVersion"));
        Assert.DoesNotContain(
            "component-ref",
            JsonApiClient.AttrString(published, "clinicalSchemaJson"),
            StringComparison.Ordinal);
        return versionId;
    }

    private async Task CompleteStage1ResponseAndAssertAuditAsync(
        string responseId,
        uint initialRowVersion)
    {
        using JsonDocument updated = await Api.PatchResourceAsync(
            "formResponses",
            responseId,
            new
            {
                answersJson = /*lang=json,strict*/ """
                    {
                      "patient.name": "Ada Lovelace",
                      "vital.bp.systolic": 120,
                      "vital.bp.diastolic": 80
                    }
                    """,
                rowVersion = initialRowVersion,
            }).ConfigureAwait(false);

        uint updatedRowVersion = JsonApiClient.AttrUInt(updated, "rowVersion");
        using JsonDocument completed = await Api.PostActionAsync(
            $"/api/formResponses/{responseId}/complete?rowVersion={updatedRowVersion}")
            .ConfigureAwait(false);
        Assert.Equal("completed", JsonApiClient.AttrString(completed, "status"));
        Assert.Equal(3u, JsonApiClient.AttrUInt(completed, "revisionNumber"));

        using JsonDocument revisions = await Api.GetAsync(
            $"/api/formResponseRevisions?filter=equals(formResponse.id,'{responseId}')"
            + "&sort=revisionNumber")
            .ConfigureAwait(false);
        JsonElement revisionOne = revisions.RootElement.GetProperty("data")
            .EnumerateArray()
            .Single(item => item.GetProperty("attributes")
                .GetProperty("revisionNumber")
                .GetInt32() == 1);
        Assert.Equal(
            "{}",
            revisionOne.GetProperty("attributes").GetProperty("answersJson").GetString());

        using JsonDocument responseAudit = await Api.GetAsync(
            "/api/auditEvents?filter=equals(resourceType,'form-response')"
            + $"&filter=equals(resourceId,'{responseId}')")
            .ConfigureAwait(false);
        Assert.Contains(
            responseAudit.RootElement.GetProperty("data").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "response.created",
                StringComparison.Ordinal));
        Assert.Contains(
            responseAudit.RootElement.GetProperty("data").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "response.updated",
                StringComparison.Ordinal));
        Assert.Contains(
            responseAudit.RootElement.GetProperty("data").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "response.completed",
                StringComparison.Ordinal));
    }

    private async Task SoftDeleteStage1DraftAndAssertAsync(
        string draftToDeleteId,
        string completedResponseId)
    {
        using HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/formResponses/{draftToDeleteId}?reason=Duplicate%20entry")
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage hiddenGet = await Api.SendGetAsync(
            $"/api/formResponses/{draftToDeleteId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, hiddenGet.StatusCode);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        Domain.Forms.FormResponse? softDeleted = await dbContext.FormResponses
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == Guid.Parse(draftToDeleteId))
            .ConfigureAwait(false);
        Assert.NotNull(softDeleted.DeletedAt);

        using JsonDocument deleteAuditEvents = await Api.GetAsync(
            "/api/auditEvents?filter=equals(resourceType,'form-response')"
            + $"&filter=equals(resourceId,'{draftToDeleteId}')")
            .ConfigureAwait(false);
        JsonElement deleteAudit = Assert.Single(
            deleteAuditEvents.RootElement.GetProperty("data").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("attributes").GetProperty("action").GetString(),
                "response.draft.deleted",
                StringComparison.Ordinal));
        Assert.Contains(
            "Duplicate entry",
            deleteAudit.GetProperty("attributes").GetProperty("metadataJson")
                .GetString(),
            StringComparison.Ordinal);

        using JsonDocument completedAfterDelete = await Api.GetAsync(
            $"/api/formResponses/{completedResponseId}").ConfigureAwait(false);
        Assert.Equal(
            "completed",
            JsonApiClient.AttrString(completedAfterDelete, "status"));
    }

    private async Task RetireStage1VersionAndAssertHistoryAsync(
        string versionId,
        string responseId)
    {
        using JsonDocument retired = await Api.PostActionAsync(
            $"/api/formVersions/{versionId}/retire",
            body: null).ConfigureAwait(false);
        Assert.Equal("retired", JsonApiClient.AttrString(retired, "status"));
        Assert.Contains(
            "patient-name",
            JsonApiClient.AttrString(retired, "clinicalSchemaJson"),
            StringComparison.Ordinal);

        using var blockedContent = JsonApiClient.CreateJsonApiContent(new
        {
            data = new
            {
                type = "formResponses",
                attributes = new { answersJson = "{}" },
                relationships = new
                {
                    formVersion = new
                    {
                        data = new { type = "formVersions", id = versionId },
                    },
                },
            },
        });
        using HttpResponseMessage blockedCreate = await Client.PostAsync(
            new Uri("/api/formResponses", UriKind.Relative),
            blockedContent).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, blockedCreate.StatusCode);

        using JsonDocument historicalAfterRetire = await Api.GetAsync(
            $"/api/formResponses/{responseId}").ConfigureAwait(false);
        Assert.Contains(
            "Ada Lovelace",
            JsonApiClient.AttrString(historicalAfterRetire, "answersJson"),
            StringComparison.Ordinal);
    }
}
