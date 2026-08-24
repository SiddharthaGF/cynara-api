using System.Net;
using System.Text.Json;

namespace Cynara.Api.Tests.ClinicalRecord;

/// <summary>
/// CYN-57 clinical record lifecycle acceptance suite (CYN-46 flow). Runs the
/// full patient → encounter → document → save → validate → complete → read
/// journey over HTTP against the real Postgres host, and asserts
/// completed-document immutability plus historical form-version rendering.
/// Tagged <c>Category=E2E</c> so it runs in the dedicated acceptance CI job.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class ClinicalRecordLifecycleAcceptanceTests : IDisposable
{
    private const string PrimaryHospitalCode =
        CynaraTenantWebApplicationFactory.PrimaryCode;

    private const string Actor = "clinician";

    public ClinicalRecordLifecycleAcceptanceTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateClient();
        Client.AcceptJsonApi();
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", PrimaryHospitalCode);
        Client.DefaultRequestHeaders.Add("X-Actor-Id", Actor);
        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();

        Api = new JsonApiClient(Client);
        Workflow = new ClinicalRecordWorkflow(Api, Client, Factory);
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task FullJourney_PatientEncounterDocumentValidateCompleteAndAudit()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync(
            "journey",
            clinicalSchemaJson: ClinicalRecordWorkflow.BpClinicalSchemaJson,
            rulesSchemaJson: ClinicalRecordWorkflow.BpValidationRulesJson)
            .ConfigureAwait(false);

        using JsonDocument search = await Workflow.SearchPatientsAsync("MRN-journey")
            .ConfigureAwait(false);
        JsonElement patient = Assert.Single(
            search.RootElement.GetProperty("patients").EnumerateArray(),
            item => string.Equals(
                item.GetProperty("id").GetString(),
                workspace.PatientId.ToString(),
                StringComparison.Ordinal));
        Assert.Equal("Ada", ClinicalRecordWorkflow.GetString(patient, "givenName"));

        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        Assert.Equal("inProgress", ClinicalRecordWorkflow.GetString(started, "status"));
        Assert.Equal(
            workspace.FormVersionId,
            Guid.Parse(ClinicalRecordWorkflow.GetString(started, "formVersionId")));
        Assert.Equal(Actor, ClinicalRecordWorkflow.GetString(started, "authorId"));
        var documentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));
        var formResponseId = Guid.Parse(
            ClinicalRecordWorkflow.GetString(started, "formResponseId"));

        const string violatingAnswers =
            /*lang=json,strict*/ """{"vital.bp.systolic":120,"vital.bp.diastolic":130}""";
        using (HttpResponseMessage saved = await Workflow.PatchBoundResponseAsync(
            formResponseId, violatingAnswers, rowVersion: 0).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        using HttpResponseMessage rejectedComplete = await Workflow
            .SendCompleteDocumentAsync(documentId, rowVersion: 0)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedComplete.StatusCode);
        string rejectionBody = await rejectedComplete.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            "BP_SYSTOLIC_GT_DIASTOLIC",
            rejectionBody,
            StringComparison.Ordinal);
        using JsonDocument stillInProgress = await Workflow.GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal(
            "inProgress",
            ClinicalRecordWorkflow.GetString(stillInProgress, "status"));

        const string validAnswers =
            /*lang=json,strict*/ """{"vital.bp.systolic":130,"vital.bp.diastolic":120}""";
        using (HttpResponseMessage saved = await Workflow.PatchBoundResponseAsync(
            formResponseId, validAnswers, rowVersion: 1).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        using JsonDocument completed = await Workflow.CompleteDocumentAsync(
            documentId, rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", ClinicalRecordWorkflow.GetString(completed, "status"));
        Assert.NotEqual(
            JsonValueKind.Null,
            completed.RootElement.GetProperty("completedAt").ValueKind);

        using JsonDocument reread = await Workflow.GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("completed", ClinicalRecordWorkflow.GetString(reread, "status"));

        await Workflow.AssertAuditAsync(
            "patient", workspace.PatientId, "patient.created", Actor).ConfigureAwait(false);
        await Workflow.AssertAuditAsync(
            "encounter", workspace.EncounterId, "encounter.created", Actor).ConfigureAwait(false);
        await Workflow.AssertAuditAsync(
            "document-definition",
            workspace.DocumentDefinitionId,
            "document-definition.created",
            Actor).ConfigureAwait(false);
        await Workflow.AssertAuditAsync(
            "clinical-document", documentId, "document.started", Actor).ConfigureAwait(false);
        await Workflow.AssertAuditAsync(
            "clinical-document", documentId, "document.completed", Actor).ConfigureAwait(false);
    }

    [Fact]
    public async Task CanceledAndEnteredInErrorStates_RemainQueryable()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync(
            "states", allowsMultipleInstancesPerEncounter: true).ConfigureAwait(false);

        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var canceledDocumentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));
        using JsonDocument canceled = await Workflow.CancelDocumentAsync(
            canceledDocumentId, rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("canceled", ClinicalRecordWorkflow.GetString(canceled, "status"));
        await Workflow.AssertAuditAsync(
            "clinical-document",
            canceledDocumentId,
            "document.canceled",
            Actor).ConfigureAwait(false);

        using JsonDocument startedForError = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var errorDocumentId = Guid.Parse(
            ClinicalRecordWorkflow.GetString(startedForError, "id"));
        using JsonDocument completed = await Workflow.CompleteDocumentAsync(
            errorDocumentId, rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", ClinicalRecordWorkflow.GetString(completed, "status"));

        using JsonDocument marked = await Workflow.EnterInErrorAsync(
            errorDocumentId,
            rowVersion: 1,
            reason: "Wrong patient transcribed").ConfigureAwait(false);
        Assert.Equal("enteredInError", ClinicalRecordWorkflow.GetString(marked, "status"));
        Assert.Equal(
            "Wrong patient transcribed",
            ClinicalRecordWorkflow.GetString(marked, "enteredInErrorReason"));
        Assert.Equal(Actor, ClinicalRecordWorkflow.GetString(marked, "enteredInErrorById"));

        using HttpResponseMessage list = await Api.SendGetAsync(
            "/api/clinicalDocuments?status=enteredInError").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDoc = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Contains(
            errorDocumentId.ToString(),
            listDoc.RootElement.GetRawText(),
            StringComparison.Ordinal);

        await Workflow.AssertAuditAsync(
            "clinical-document",
            errorDocumentId,
            "document.enteredInError",
            Actor).ConfigureAwait(false);
    }

    [Fact]
    public async Task CompletedDocument_IsImmutableAndCannotBePhysicallyDeleted()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("immutable")
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var documentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));
        var formResponseId = Guid.Parse(
            ClinicalRecordWorkflow.GetString(started, "formResponseId"));

        const string answers =
            /*lang=json,strict*/ """{"cr.immutable":"final"}""";
        using (HttpResponseMessage saved = await Workflow.PatchBoundResponseAsync(
            formResponseId, answers, rowVersion: 0).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        using JsonDocument completed = await Workflow.CompleteDocumentAsync(
            documentId, rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", ClinicalRecordWorkflow.GetString(completed, "status"));

        using HttpResponseMessage editLocked = await Workflow.PatchBoundResponseAsync(
            formResponseId, answers, rowVersion: 1).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, editLocked.StatusCode);

        using HttpResponseMessage deleteLocked = await Workflow
            .SoftDeleteResponseAsync(formResponseId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, deleteLocked.StatusCode);

        using HttpResponseMessage deleteDocument = await Api.DeleteAsync(
            $"/api/clinicalDocuments/{documentId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteDocument.StatusCode);
        Assert.True(
            await Workflow.ClinicalDocumentExistsAsync(documentId).ConfigureAwait(false),
            "Completed document must remain persisted after a delete attempt.");

        using JsonDocument reread = await Workflow.GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("completed", ClinicalRecordWorkflow.GetString(reread, "status"));
        using var answersDoc = JsonDocument.Parse(
            await Workflow.GetFormResponseAnswersAsync(formResponseId)
                .ConfigureAwait(false));
        Assert.Equal(
            "final",
            answersDoc.RootElement.GetProperty("cr.immutable").GetString());
    }

    [Fact]
    public async Task HistoricalFormVersion_CompletedDocumentRendersPinnedVersion()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync(
            "historical",
            allowsMultipleInstancesPerEncounter: true).ConfigureAwait(false);
        Guid v1Id = workspace.FormVersionId;

        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var documentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));
        var formResponseId = Guid.Parse(
            ClinicalRecordWorkflow.GetString(started, "formResponseId"));

        const string answersV1 =
            /*lang=json,strict*/ """{"cr.historical":"Historical Ada"}""";
        using (HttpResponseMessage saved = await Workflow.PatchBoundResponseAsync(
            formResponseId, answersV1, rowVersion: 0).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        using JsonDocument completed = await Workflow.CompleteDocumentAsync(
            documentId, rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", ClinicalRecordWorkflow.GetString(completed, "status"));

        string clinicalV2 = JsonApiWorkflow.MinimalClinicalSchema(
            "historical-v2", "cr.historical-v2");
        using JsonDocument publishedV2 = await Workflow.PublishNextFormVersionAsync(
            workspace.FormDefinitionId, clinicalV2).ConfigureAwait(false);
        Assert.Equal("1.0.1", JsonApiClient.AttrString(publishedV2, "version"));
        var v2Id = Guid.Parse(JsonApiClient.RequireId(publishedV2));
        Assert.NotEqual(v1Id, v2Id);

        using JsonDocument reread = await Workflow.GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal(
            v1Id,
            Guid.Parse(ClinicalRecordWorkflow.GetString(reread, "formVersionId")));
        using var answersDoc = JsonDocument.Parse(
            await Workflow.GetFormResponseAnswersAsync(formResponseId)
                .ConfigureAwait(false));
        Assert.Equal(
            "Historical Ada",
            answersDoc.RootElement.GetProperty("cr.historical").GetString());

        using JsonDocument frozenV1 = await Api.GetAsync($"/api/formVersions/{v1Id}")
            .ConfigureAwait(false);
        Assert.Contains(
            "historical-field",
            JsonApiClient.AttrString(frozenV1, "clinicalSchemaJson"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "historical-v2",
            JsonApiClient.AttrString(frozenV1, "clinicalSchemaJson"),
            StringComparison.Ordinal);

        Guid definitionV2 = await Workflow.CreateDocumentDefinitionAsync(
            "cr-def-historical-v2",
            "Document v2",
            workspace.FormDefinitionId,
            v2Id,
            workspace.FacilityId,
            workspace.ClinicalAreaId,
            workspace.DisciplineId).ConfigureAwait(false);
        using JsonDocument startedV2 = await Workflow.StartDocumentAsync(
            definitionV2, workspace.EncounterId).ConfigureAwait(false);
        Assert.Equal(
            v2Id,
            Guid.Parse(ClinicalRecordWorkflow.GetString(startedV2, "formVersionId")));
    }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private ClinicalRecordWorkflow Workflow { get; }

    private CynaraTenantWebApplicationFactory Factory { get; }
}
