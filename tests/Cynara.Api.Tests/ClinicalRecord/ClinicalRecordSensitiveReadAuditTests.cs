using System.Net;
using System.Text.Json;

using Cynara.Domain.Audit;

namespace Cynara.Api.Tests.ClinicalRecord;

/// <summary>
/// CYN-45 sensitive-read audit coverage. Successful reads of patient,
/// encounter, clinical document, and form response records emit a scoped
/// <c>*.read</c> audit event whose metadata carries only the request path
/// (never the clinical payload). List reads and internal re-reads during
/// create/update flows do not emit read events.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ClinicalRecordSensitiveReadAuditTests : IAsyncDisposable
{
    private const string PrimaryHospitalCode =
        CynaraTenantWebApplicationFactory.PrimaryCode;

    private const string Actor = "read-auditor";

    public ClinicalRecordSensitiveReadAuditTests(PostgreSqlDatabaseFixture database)
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

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PatientRead_EmitsSensitiveReadAuditEvent()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("read-patient")
            .ConfigureAwait(false);

        using JsonDocument response = await Api.GetAsync(
            $"/api/patients/{workspace.PatientId}").ConfigureAwait(false);
        Assert.Equal(
            workspace.PatientId.ToString(),
            ClinicalRecordWorkflow.GetString(response, "id"));

        await Workflow.AssertAuditAsync(
            "patient", workspace.PatientId, "patient.read", Actor).ConfigureAwait(false);

        string metadata = await GetAuditMetadataAsync(
            "patient", workspace.PatientId, "patient.read").ConfigureAwait(false);
        Assert.Contains(
            "\"requestPath\":\"/api/patients/",
            metadata,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Ada", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EncounterRead_EmitsSensitiveReadAuditEvent()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("read-encounter")
            .ConfigureAwait(false);

        using JsonDocument response = await Api.GetAsync(
            $"/api/encounters/{workspace.EncounterId}").ConfigureAwait(false);
        Assert.Equal(
            workspace.EncounterId.ToString(),
            ClinicalRecordWorkflow.GetString(response, "id"));

        await Workflow.AssertAuditAsync(
            "encounter", workspace.EncounterId, "encounter.read", Actor).ConfigureAwait(false);
    }

    [Fact]
    public async Task ClinicalDocumentRead_EmitsSensitiveReadAuditEvent()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("read-document")
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var documentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));

        using JsonDocument response = await Workflow.GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal(
            documentId.ToString(),
            ClinicalRecordWorkflow.GetString(response, "id"));

        await Workflow.AssertAuditAsync(
            "clinical-document", documentId, "document.read", Actor).ConfigureAwait(false);
    }

    [Fact]
    public async Task FormResponseRead_EmitsReadEventWithoutClinicalPayload()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync(
            "read-response",
            clinicalSchemaJson: ClinicalRecordWorkflow.BpClinicalSchemaJson)
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var formResponseId = Guid.Parse(
            ClinicalRecordWorkflow.GetString(started, "formResponseId"));

        const string answers = /*lang=json,strict*/
            """{"vital.bp.systolic":130,"vital.bp.diastolic":120}""";
        using (HttpResponseMessage saved = await Workflow.PatchBoundResponseAsync(
            formResponseId, answers, rowVersion: 0).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        Assert.Equal(
            0,
            await Workflow.CountAuditEventsAsync("response.read").ConfigureAwait(false));

        using JsonDocument response = await Api.GetAsync(
            $"/api/formResponses/{formResponseId}").ConfigureAwait(false);
        Assert.Equal(formResponseId.ToString(), JsonApiClient.RequireId(response));

        await Workflow.AssertAuditAsync(
            "form-response", formResponseId, "response.read", Actor).ConfigureAwait(false);

        string metadata = await GetAuditMetadataAsync(
            "form-response", formResponseId, "response.read").ConfigureAwait(false);
        Assert.DoesNotContain("answersJson", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("vital.bp", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatientSearch_DoesNotEmitReadAuditEvents()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("read-search")
            .ConfigureAwait(false);

        using JsonDocument search = await Workflow.SearchPatientsAsync("MRN-read-search")
            .ConfigureAwait(false);
        Assert.Contains(
            workspace.PatientId.ToString(),
            search.RootElement.GetRawText(),
            StringComparison.Ordinal);

        Assert.Equal(
            0,
            await Workflow.CountAuditEventsAsync("patient.read").ConfigureAwait(false));
    }

    [Fact]
    public async Task CollectionReads_DoNotEmitReadAuditEvents()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("read-collection")
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        Assert.NotNull(started);

        using (HttpResponseMessage encounters = await Client.GetAsync(
            new Uri("/api/encounters", UriKind.Relative)).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, encounters.StatusCode);
        }

        using (HttpResponseMessage documents = await Client.GetAsync(
            new Uri("/api/clinicalDocuments", UriKind.Relative)).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, documents.StatusCode);
        }

        using JsonDocument responses = await Api.GetAsync("/api/formResponses")
            .ConfigureAwait(false);
        Assert.True(responses.RootElement.TryGetProperty("data", out _));

        Assert.Equal(
            0,
            await Workflow.CountAuditEventsAsync("encounter.read").ConfigureAwait(false));
        Assert.Equal(
            0,
            await Workflow.CountAuditEventsAsync("document.read").ConfigureAwait(false));
        Assert.Equal(
            0,
            await Workflow.CountAuditEventsAsync("response.read").ConfigureAwait(false));
    }

    private async Task<string> GetAuditMetadataAsync(
        string resourceType,
        Guid resourceId,
        string action)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        AuditEvent auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.ResourceType == resourceType
                && item.ResourceId == resourceId
                && item.Action == action)
            .ConfigureAwait(false);
        return auditEvent.MetadataJson ?? string.Empty;
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private JsonApiClient Api { get; }

    private ClinicalRecordWorkflow Workflow { get; }
}
