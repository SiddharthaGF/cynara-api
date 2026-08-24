using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;

namespace Cynara.Api.Tests.ClinicalRecord;

/// <summary>
/// CYN-57 tenant-isolation attack matrix for the clinical record lifecycle.
/// A secondary hospital cannot enumerate, directly read, query through
/// nested resources, or substitute foreign keys owned by the primary
/// hospital, and every rejected write leaves no state change behind.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ClinicalRecordTenantIsolationAttackTests : IAsyncDisposable
{
    private const string PrimaryHospitalCode =
        CynaraTenantWebApplicationFactory.PrimaryCode;

    private const string OtherHospitalCode =
        CynaraTenantWebApplicationFactory.OtherCode;

    public ClinicalRecordTenantIsolationAttackTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        PrimaryClient = Factory.CreateAuthenticatedClientAsync(
            actorId: "primary-clinician",
            hospitalCode: PrimaryHospitalCode).GetAwaiter().GetResult();
        OtherClient = Factory.CreateAuthenticatedClientAsync(
            actorId: "other-clinician",
            hospitalCode: OtherHospitalCode).GetAwaiter().GetResult();

        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();

        Workflow = new ClinicalRecordWorkflow(
            new JsonApiClient(PrimaryClient), PrimaryClient, Factory);
    }

    public async ValueTask DisposeAsync()
    {
        PrimaryClient.Dispose();
        OtherClient.Dispose();
        await Factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Enumeration_SecondaryHospital_ListsExcludePrimaryResources()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("enum")
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var documentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));

        using JsonDocument patients = await OtherApi()
            .GetAsync("/api/patients").ConfigureAwait(false);
        Assert.DoesNotContain(
            patients.RootElement.GetRawText(),
            workspace.PatientId.ToString(),
            StringComparison.Ordinal);

        using JsonDocument encounters = await OtherApi()
            .GetAsync("/api/encounters").ConfigureAwait(false);
        Assert.Equal(
            0,
            encounters.RootElement.GetProperty("encounters").GetArrayLength());

        using JsonDocument documents = await OtherApi()
            .GetAsync("/api/clinicalDocuments").ConfigureAwait(false);
        Assert.Equal(
            0,
            documents.RootElement.GetProperty("documents").GetArrayLength());
        Assert.DoesNotContain(
            documents.RootElement.GetRawText(),
            documentId.ToString(),
            StringComparison.Ordinal);

        using JsonDocument definitions = await OtherApi()
            .GetAsync("/api/documentDefinitions").ConfigureAwait(false);
        Assert.DoesNotContain(
            definitions.RootElement.GetRawText(),
            workspace.DocumentDefinitionId.ToString(),
            StringComparison.Ordinal);

        using JsonDocument facilities = await OtherApi()
            .GetAsync("/api/facilities").ConfigureAwait(false);
        Assert.DoesNotContain(
            facilities.RootElement.GetRawText(),
            workspace.FacilityId.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectLookup_SecondaryHospital_ReturnsNotFound()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("lookup")
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var documentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));

        using HttpResponseMessage patient = await OtherClient
            .GetAsync(new Uri($"/api/patients/{workspace.PatientId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, patient.StatusCode);

        using HttpResponseMessage encounter = await OtherClient
            .GetAsync(new Uri($"/api/encounters/{workspace.EncounterId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, encounter.StatusCode);

        using HttpResponseMessage document = await OtherClient
            .GetAsync(new Uri($"/api/clinicalDocuments/{documentId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);

        using HttpResponseMessage definition = await OtherClient
            .GetAsync(new Uri(
                $"/api/documentDefinitions/{workspace.DocumentDefinitionId}",
                UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, definition.StatusCode);
    }

    [Fact]
    public async Task NestedResources_SecondaryHospital_QueriesDoNotLeakPrimaryData()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("nested")
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var documentId = Guid.Parse(ClinicalRecordWorkflow.GetString(started, "id"));
        var formResponseId = Guid.Parse(
            ClinicalRecordWorkflow.GetString(started, "formResponseId"));
        using JsonDocument completed = await Workflow.CompleteDocumentAsync(
            documentId, rowVersion: 0).ConfigureAwait(false);
        Assert.Equal("completed", ClinicalRecordWorkflow.GetString(completed, "status"));

        using JsonDocument byEncounter = await OtherApi()
            .GetAsync($"/api/clinicalDocuments?encounterId={workspace.EncounterId}")
            .ConfigureAwait(false);
        Assert.Equal(
            0,
            byEncounter.RootElement.GetProperty("documents").GetArrayLength());

        using JsonDocument responses = await OtherApi()
            .GetAsync("/api/formResponses").ConfigureAwait(false);
        Assert.DoesNotContain(
            responses.RootElement.GetRawText(),
            formResponseId.ToString(),
            StringComparison.Ordinal);

        using JsonDocument audit = await OtherApi()
            .GetAsync($"/api/auditEvents?filter=equals(resourceId,'{documentId}')")
            .ConfigureAwait(false);
        Assert.Empty(audit.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task ForeignKeySubstitution_SecondaryHospital_WritesRejectedWithoutStateChange()
    {
        ClinicalWorkspace workspace = await Workflow.BuildWorkspaceAsync("fksub")
            .ConfigureAwait(false);
        using JsonDocument started = await Workflow.StartDocumentAsync(
            workspace.DocumentDefinitionId,
            workspace.EncounterId).ConfigureAwait(false);
        var primaryDocumentId = Guid.Parse(
            ClinicalRecordWorkflow.GetString(started, "id"));

        int encountersBefore = await Workflow.CountAsync<Encounter>()
            .ConfigureAwait(false);
        int documentsBefore = await Workflow.CountAsync<ClinicalDocument>()
            .ConfigureAwait(false);
        int definitionsBefore = await Workflow.CountAsync<DocumentDefinition>()
            .ConfigureAwait(false);

        using HttpResponseMessage substitutedEncounter = await OtherClient
            .SendAsync(ClinicalRecordWorkflow.PostJsonRequest(
                "/api/encounters",
                new
                {
                    patientId = workspace.PatientId,
                    facilityId = workspace.FacilityId,
                    clinicalAreaId = workspace.ClinicalAreaId,
                    type = "ambulatory",
                    responsibleProfessionalId = "dr-other",
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, substitutedEncounter.StatusCode);

        using HttpResponseMessage substitutedDocument = await OtherClient
            .SendAsync(ClinicalRecordWorkflow.PostJsonRequest(
                "/api/clinicalDocuments",
                new
                {
                    documentDefinitionId = workspace.DocumentDefinitionId,
                    encounterId = workspace.EncounterId,
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, substitutedDocument.StatusCode);

        using HttpResponseMessage substitutedDefinition = await OtherClient
            .SendAsync(BuildDocumentDefinitionCreateRequest(workspace))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, substitutedDefinition.StatusCode);

        Assert.Equal(
            encountersBefore,
            await Workflow.CountAsync<Encounter>().ConfigureAwait(false));
        Assert.Equal(
            documentsBefore,
            await Workflow.CountAsync<ClinicalDocument>().ConfigureAwait(false));
        Assert.Equal(
            definitionsBefore,
            await Workflow.CountAsync<DocumentDefinition>().ConfigureAwait(false));
        Assert.True(
            await Workflow.ClinicalDocumentExistsAsync(primaryDocumentId).ConfigureAwait(false),
            "Primary document must be untouched by the rejected secondary writes.");
    }

    private JsonApiClient OtherApi()
    {
        return new JsonApiClient(OtherClient);
    }

    private static HttpRequestMessage BuildDocumentDefinitionCreateRequest(
        ClinicalWorkspace workspace)
    {
        var payload = new
        {
            data = new
            {
                type = "documentDefinitions",
                attributes = new
                {
                    code = "cr-def-fksub-other",
                    name = "Foreign-key substitution",
                    allowsMultipleInstancesPerEncounter = true,
                    requiresActorForCreation = true,
                    requiresActorForCompletion = true,
                },
                relationships = new
                {
                    formDefinition = new
                    {
                        data = new
                        {
                            type = "formDefinitions",
                            id = workspace.FormDefinitionId,
                        },
                    },
                    formVersion = new
                    {
                        data = new
                        {
                            type = "formVersions",
                            id = workspace.FormVersionId,
                        },
                    },
                    facility = new
                    {
                        data = new { type = "facilities", id = workspace.FacilityId },
                    },
                    clinicalArea = new
                    {
                        data = new
                        {
                            type = "clinicalAreas",
                            id = workspace.ClinicalAreaId,
                        },
                    },
                    discipline = new
                    {
                        data = new
                        {
                            type = "disciplines",
                            id = workspace.DisciplineId,
                        },
                    },
                },
            },
        };
        return new HttpRequestMessage(
            HttpMethod.Post, new Uri("/api/documentDefinitions", UriKind.Relative))
        {
            Content = JsonApiClient.CreateJsonApiContent(payload),
        };
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient PrimaryClient { get; }

    private HttpClient OtherClient { get; }

    private ClinicalRecordWorkflow Workflow { get; }
}
