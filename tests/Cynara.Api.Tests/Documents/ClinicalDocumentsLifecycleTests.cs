using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Cynara.Domain.Audit;
using Cynara.Domain.Documents;
using Cynara.Domain.Forms;

namespace Cynara.Api.Tests.Documents;

/// <summary>
/// CYN-39 clinical document instance lifecycle integration tests. Covers
/// start / get / list, published form version pinning, the bound form
/// response, the single/multiple-instance catalog policy, retired
/// definitions, non-open encounters, actor attribution, and tenant
/// isolation.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ClinicalDocumentsLifecycleTests : IDisposable
{
    private const string ContentType = "application/vnd.api+json";
    private const string PrimaryHospitalCode = "primary";
    private const string OtherHospitalCode = "secondary";

    public ClinicalDocumentsLifecycleTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Client = Factory.CreateAuthenticatedClientAsync(
            actorId: "clinician",
            hospitalCode: PrimaryHospitalCode).GetAwaiter().GetResult();
        OtherClient = Factory.CreateAuthenticatedClientAsync(
            hospitalCode: OtherHospitalCode).GetAwaiter().GetResult();

        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();

        Api = new JsonApiClient(Client);
        Workflow = new JsonApiWorkflow(Api, Client);
    }

    public void Dispose()
    {
        Client.Dispose();
        OtherClient.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task StartDocument_PersistsAndAudits()
    {
        DocumentFixture fixture = await SeedFixtureAsync("start")
            .ConfigureAwait(false);

        using JsonDocument created = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);

        Assert.Equal("inProgress", GetString(created, "status"));
        Assert.Equal(
            fixture.DocumentDefinitionId,
            Guid.Parse(GetString(created, "documentDefinitionId")));
        Assert.Equal(
            fixture.PatientId,
            Guid.Parse(GetString(created, "patientId")));
        Assert.Equal(
            fixture.EncounterId,
            Guid.Parse(GetString(created, "encounterId")));
        Assert.Equal(
            fixture.FormVersionId,
            Guid.Parse(GetString(created, "formVersionId")));
        Assert.Equal("clinician", GetString(created, "authorId"));
        var formResponseId = Guid.Parse(GetString(created, "formResponseId"));
        Assert.Equal(
            JsonValueKind.Null,
            created.RootElement.GetProperty("completedAt").ValueKind);
        Assert.Equal(0u, created.RootElement.GetProperty("rowVersion").GetUInt32());

        using JsonDocument fetched = await GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("inProgress", GetString(fetched, "status"));

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri(
                $"/api/clinicalDocuments?encounterId={fixture.EncounterId}",
                UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDoc = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Contains(
            documentId.ToString(),
            listDoc.RootElement.GetRawText(),
            StringComparison.Ordinal);

        await AssertAuditAsync(documentId, "document.started")
            .ConfigureAwait(false);
        await AssertBoundResponseAsync(
                formResponseId, fixture.FormVersionId)
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task StartDocument_RejectsDuplicateForSingleInstanceCatalog()
    {
        DocumentFixture fixture = await SeedFixtureAsync("single")
            .ConfigureAwait(false);
        await StartDocumentAsync(fixture).ConfigureAwait(false);

        using HttpResponseMessage duplicate = await SendStartAsync(fixture)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        string body = await duplicate.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains(
            "single document per encounter", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartDocument_AllowsMultipleForMultiInstanceCatalog()
    {
        DocumentFixture fixture = await SeedFixtureAsync(
            "multi", allowsMultipleInstancesPerEncounter: true)
            .ConfigureAwait(false);
        await StartDocumentAsync(fixture).ConfigureAwait(false);

        using JsonDocument second = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        Assert.Equal(
            fixture.EncounterId,
            Guid.Parse(GetString(second, "encounterId")));
    }

    [Fact]
    public async Task StartDocument_RejectsRetiredDefinition()
    {
        DocumentFixture fixture = await SeedFixtureAsync("retired")
            .ConfigureAwait(false);
        using HttpResponseMessage retire = await Client.PostAsync(
            new Uri(
                $"/api/documentDefinitions/{fixture.DocumentDefinitionId}"
                + "/retire?rowVersion=0",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, retire.StatusCode);

        using HttpResponseMessage rejected = await SendStartAsync(fixture)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        string body = await rejected.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("retired", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartDocument_RejectsNonOpenEncounter()
    {
        DocumentFixture fixture = await SeedFixtureAsync("closed")
            .ConfigureAwait(false);
        using HttpResponseMessage complete = await Client.SendAsync(
            PostJsonRequest(
                $"/api/encounters/{fixture.EncounterId}/complete",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using HttpResponseMessage rejected = await SendStartAsync(fixture)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        string body = await rejected.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        Assert.Contains("open", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartDocument_RejectsUnknownReferences()
    {
        DocumentFixture fixture = await SeedFixtureAsync("unknown")
            .ConfigureAwait(false);

        using HttpResponseMessage unknownDefinition = await Client
            .SendAsync(PostJsonRequest(
                "/api/clinicalDocuments",
                new
                {
                    documentDefinitionId = Guid.NewGuid(),
                    encounterId = fixture.EncounterId,
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, unknownDefinition.StatusCode);

        using HttpResponseMessage unknownEncounter = await Client
            .SendAsync(PostJsonRequest(
                "/api/clinicalDocuments",
                new
                {
                    documentDefinitionId = fixture.DocumentDefinitionId,
                    encounterId = Guid.NewGuid(),
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, unknownEncounter.StatusCode);
    }

    [Fact]
    public async Task StartDocument_RequiresActorWhenCatalogDemandsIt()
    {
        DocumentFixture fixture = await SeedFixtureAsync("actor")
            .ConfigureAwait(false);

        using HttpClient anonymous = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: PrimaryHospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage rejected = await anonymous.SendAsync(
            PostJsonRequest(
                "/api/clinicalDocuments",
                new
                {
                    documentDefinitionId = fixture.DocumentDefinitionId,
                    encounterId = fixture.EncounterId,
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_StartDocument_IsHidden()
    {
        DocumentFixture fixture = await SeedFixtureAsync("tenant")
            .ConfigureAwait(false);
        using JsonDocument created = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            created.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage otherGet = await OtherClient
            .GetAsync(new Uri(
                $"/api/clinicalDocuments/{documentId}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);

        using HttpResponseMessage otherStart = await OtherClient
            .SendAsync(PostJsonRequest(
                "/api/clinicalDocuments",
                new
                {
                    documentDefinitionId = fixture.DocumentDefinitionId,
                    encounterId = fixture.EncounterId,
                }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherStart.StatusCode);
    }

    [Fact]
    public async Task CompleteDocument_CompletesBoundResponseAndLocksEdits()
    {
        DocumentFixture fixture = await SeedFixtureAsync("complete")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);
        var formResponseId = Guid.Parse(
            GetString(started, "formResponseId"));
        const string fieldCode = "code-complete";

        using HttpResponseMessage update = await UpdateBoundResponseAsync(
            formResponseId, fieldCode, "Ada", rowVersion: 0)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        using HttpResponseMessage complete = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var completed = JsonDocument.Parse(
            await complete.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal("completed", GetString(completed, "status"));
        Assert.NotEqual(
            JsonValueKind.Null,
            completed.RootElement.GetProperty("completedAt").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            completed.RootElement.GetProperty("canceledAt").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            completed.RootElement.GetProperty("enteredInErrorAt").ValueKind);
        Assert.Equal(1u, completed.RootElement.GetProperty("rowVersion").GetUInt32());

        using JsonDocument fetched = await GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("completed", GetString(fetched, "status"));

        using HttpResponseMessage editLocked = await PatchBoundResponseAsync(
            formResponseId, fieldCode, "Changed", rowVersion: 1)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, editLocked.StatusCode);

        await AssertAuditAsync(documentId, "document.completed")
            .ConfigureAwait(false);
        await AssertBoundResponseCompletedAsync(formResponseId)
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task CompleteDocument_RejectsStaleConcurrency()
    {
        DocumentFixture fixture = await SeedFixtureAsync("stale")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage stale = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion = 99U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using JsonDocument fetched = await GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("inProgress", GetString(fetched, "status"));
    }

    [Fact]
    public async Task CompleteDocument_RejectsAlreadyCompleted()
    {
        DocumentFixture fixture = await SeedFixtureAsync("twice")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage first = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage second = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion = 1U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CompleteDocument_RequiresActorWhenCatalogDemandsIt()
    {
        DocumentFixture fixture = await SeedFixtureAsync("complete-actor")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);

        using HttpClient anonymous = await Factory
            .CreateAuthenticatedClientAsync(hospitalCode: PrimaryHospitalCode)
            .ConfigureAwait(false);
        using HttpResponseMessage rejected = await anonymous.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task CancelDocument_CancelsAndKeepsResponseDraft()
    {
        DocumentFixture fixture = await SeedFixtureAsync("cancel")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);
        var formResponseId = Guid.Parse(
            GetString(started, "formResponseId"));

        using HttpResponseMessage cancel = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/cancel",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using var canceled = JsonDocument.Parse(
            await cancel.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal("canceled", GetString(canceled, "status"));
        Assert.NotEqual(
            JsonValueKind.Null,
            canceled.RootElement.GetProperty("canceledAt").ValueKind);

        using JsonDocument fetched = await GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("canceled", GetString(fetched, "status"));

        await AssertAuditAsync(documentId, "document.canceled")
            .ConfigureAwait(false);
        await AssertBoundResponseDraftAsync(formResponseId)
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task EnterInError_FromCompleted_RemainsQueryableWithAttribution()
    {
        DocumentFixture fixture = await SeedFixtureAsync("eie")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage complete = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using HttpResponseMessage mark = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/enter-in-error",
                new { rowVersion = 1U, reason = "Wrong result transcribed" }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
        using var marked = JsonDocument.Parse(
            await mark.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal("enteredInError", GetString(marked, "status"));
        Assert.Equal(
            "Wrong result transcribed",
            GetString(marked, "enteredInErrorReason"));
        Assert.Equal("clinician", GetString(marked, "enteredInErrorById"));
        Assert.NotEqual(
            JsonValueKind.Null,
            marked.RootElement.GetProperty("enteredInErrorAt").ValueKind);

        using JsonDocument fetched = await GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("enteredInError", GetString(fetched, "status"));

        using HttpResponseMessage list = await Client
            .GetAsync(new Uri(
                "/api/clinicalDocuments?status=enteredInError",
                UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDoc = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Contains(
            documentId.ToString(),
            listDoc.RootElement.GetRawText(),
            StringComparison.Ordinal);

        await AssertAuditAsync(documentId, "document.enteredInError")
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task EnterInError_RequiresReason()
    {
        DocumentFixture fixture = await SeedFixtureAsync("eie-reason")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage rejected = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/enter-in-error",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using JsonDocument fetched = await GetDocumentAsync(documentId)
            .ConfigureAwait(false);
        Assert.Equal("inProgress", GetString(fetched, "status"));
    }

    [Fact]
    public async Task CrossTenant_CompleteDocument_IsHidden()
    {
        DocumentFixture fixture = await SeedFixtureAsync("ct-complete")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage otherComplete = await OtherClient
            .SendAsync(PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, otherComplete.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByCanceledAndEnteredInError()
    {
        DocumentFixture fixture = await SeedFixtureAsync("list-states")
            .ConfigureAwait(false);
        using JsonDocument started = await StartDocumentAsync(fixture)
            .ConfigureAwait(false);
        var documentId = Guid.Parse(
            started.RootElement.GetProperty("id").GetString()!);

        using HttpResponseMessage canceled = await Client.SendAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/cancel",
                new { rowVersion = 0U }))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);

        using HttpResponseMessage canceledList = await Client
            .GetAsync(new Uri(
                "/api/clinicalDocuments?status=canceled", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, canceledList.StatusCode);
        using var canceledDoc = JsonDocument.Parse(
            await canceledList.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Contains(
            documentId.ToString(),
            canceledDoc.RootElement.GetRawText(),
            StringComparison.Ordinal);

        using HttpResponseMessage emptyInProgress = await Client
            .GetAsync(new Uri(
                "/api/clinicalDocuments?status=inProgress",
                UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, emptyInProgress.StatusCode);
        using var inProgressDoc = JsonDocument.Parse(
            await emptyInProgress.Content.ReadAsStringAsync()
                .ConfigureAwait(false));
        Assert.DoesNotContain(
            documentId.ToString(),
            inProgressDoc.RootElement.GetRawText(),
            StringComparison.Ordinal);
    }

    private async Task<DocumentFixture> SeedFixtureAsync(
        string suffix,
        bool allowsMultipleInstancesPerEncounter = false)
    {
        using JsonDocument facility = await PostRawJsonAsync(
            "facilities",
            new { code = $"cdoc-fac-{suffix}", name = $"Facility {suffix}" })
            .ConfigureAwait(false);
        var facilityId = Guid.Parse(
            facility.RootElement.GetProperty("id").GetString()!);

        using JsonDocument area = await PostRawJsonAsync(
            "clinicalAreas",
            new
            {
                code = $"cdoc-area-{suffix}",
                name = $"Area {suffix}",
                facilityId,
            })
            .ConfigureAwait(false);
        var clinicalAreaId = Guid.Parse(
            area.RootElement.GetProperty("id").GetString()!);

        using JsonDocument discipline = await PostRawJsonAsync(
            "disciplines",
            new
            {
                code = $"cdoc-disc-{suffix}",
                name = $"Discipline {suffix}",
                clinicalAreaId,
            })
            .ConfigureAwait(false);
        var disciplineId = Guid.Parse(
            discipline.RootElement.GetProperty("id").GetString()!);

        (string definitionId, string formVersionId) = await Workflow
            .PublishFormAsync(
                $"cdoc-form-{suffix}",
                $"Form {suffix}",
                JsonApiWorkflow.MinimalClinicalSchema(
                    $"field-{suffix}", $"code-{suffix}"))
            .ConfigureAwait(false);

        using JsonDocument catalogEntry = await Api.PostResourceAsync(
            "documentDefinitions",
            new
            {
                code = $"cdoc-def-{suffix}",
                name = $"Document {suffix}",
                allowsMultipleInstancesPerEncounter,
                requiresActorForCreation = true,
                requiresActorForCompletion = true,
            },
            new
            {
                formDefinition = new
                {
                    data = new { type = "formDefinitions", id = definitionId },
                },
                formVersion = new
                {
                    data = new { type = "formVersions", id = formVersionId },
                },
                facility = new
                {
                    data = new { type = "facilities", id = facilityId },
                },
                clinicalArea = new
                {
                    data = new { type = "clinicalAreas", id = clinicalAreaId },
                },
                discipline = new
                {
                    data = new { type = "disciplines", id = disciplineId },
                },
            }).ConfigureAwait(false);
        var documentDefinitionId = Guid.Parse(
            catalogEntry.RootElement.GetProperty("data")
                .GetProperty("id").GetString()!);

        using JsonDocument patient = await PostRawJsonAsync(
            "patients",
            new
            {
                mrn = $"MRN-{suffix}",
                givenName = "Ada",
                familyName = "Lovelace",
                birthDate = "1990-01-01",
                sex = "female",
                bloodType = "o+",
            })
            .ConfigureAwait(false);
        var patientId = Guid.Parse(
            patient.RootElement.GetProperty("id").GetString()!);

        using JsonDocument encounter = await PostRawJsonAsync(
            "encounters",
            new
            {
                patientId,
                facilityId,
                clinicalAreaId,
                type = "ambulatory",
                responsibleProfessionalId = "dr-who",
            })
            .ConfigureAwait(false);
        var encounterId = Guid.Parse(
            encounter.RootElement.GetProperty("id").GetString()!);

        return new DocumentFixture(
            patientId,
            encounterId,
            Guid.Parse(definitionId),
            Guid.Parse(formVersionId),
            documentDefinitionId);
    }

    private async Task<JsonDocument> StartDocumentAsync(DocumentFixture fixture)
    {
        using HttpResponseMessage response = await SendStartAsync(fixture)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            string body = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Start failed with {(int)response.StatusCode}: {body}"));
        }

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private Task<HttpResponseMessage> SendStartAsync(DocumentFixture fixture)
    {
        return Client.SendAsync(
            PostJsonRequest(
                "/api/clinicalDocuments",
                new
                {
                    documentDefinitionId = fixture.DocumentDefinitionId,
                    encounterId = fixture.EncounterId,
                }));
    }

    private async Task<JsonDocument> GetDocumentAsync(Guid id)
    {
        using HttpResponseMessage response = await Client
            .GetAsync(new Uri($"/api/clinicalDocuments/{id}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private async Task<JsonDocument> PostRawJsonAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri($"/api/{path}", UriKind.Relative),
            new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                ContentType)).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"HTTP {(int)response.StatusCode}: {text}"));
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static HttpRequestMessage PostJsonRequest(string path, object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8)
            {
                Headers =
                {
                    ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType),
                },
            },
        };
    }

    private Task<HttpResponseMessage> UpdateBoundResponseAsync(
        Guid formResponseId,
        string fieldCode,
        string value,
        uint rowVersion)
    {
        return PatchBoundResponseAsync(
            formResponseId, fieldCode, value, rowVersion);
    }

    private Task<HttpResponseMessage> PatchBoundResponseAsync(
        Guid formResponseId,
        string fieldCode,
        string value,
        uint rowVersion)
    {
        string answersJson = JsonSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [fieldCode] = value,
            });
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formResponses/{formResponseId}", UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        type = "formResponses",
                        id = formResponseId,
                        attributes = new
                        {
                            answersJson,
                            rowVersion,
                        },
                    },
                }),
                Encoding.UTF8)
            {
                Headers =
                {
                    ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType),
                },
            },
        };
        return Client.SendAsync(request);
    }

    private static string GetString(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty(name).GetString() ?? string.Empty;
    }

    private async Task AssertAuditAsync(Guid documentId, string action)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        AuditEvent createdEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.ResourceType == "clinical-document"
                && item.ResourceId == documentId
                && item.Action == action)
            .ConfigureAwait(false);
        Assert.Equal("clinician", createdEvent.ActorId);
    }

    private async Task AssertBoundResponseAsync(
        Guid formResponseId,
        Guid formVersionId)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        FormResponse response = await dbContext.FormResponses
            .AsNoTracking()
            .SingleAsync(item => item.Id == formResponseId)
            .ConfigureAwait(false);
        Assert.Equal(formVersionId, response.FormVersionId);
        Assert.Equal(FormResponseStatus.Draft, response.Status);
        Assert.Equal(1u, response.RevisionNumber);

        int revisions = await dbContext.FormResponseRevisions
            .AsNoTracking()
            .CountAsync(item => item.FormResponseId == formResponseId)
            .ConfigureAwait(false);
        Assert.Equal(1, revisions);

        int documents = await dbContext.Set<ClinicalDocument>()
            .AsNoTracking()
            .CountAsync(item => item.FormResponseId == formResponseId)
            .ConfigureAwait(false);
        Assert.Equal(1, documents);
    }

    private async Task AssertBoundResponseCompletedAsync(Guid formResponseId)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        FormResponse response = await dbContext.FormResponses
            .AsNoTracking()
            .SingleAsync(item => item.Id == formResponseId)
            .ConfigureAwait(false);
        Assert.Equal(FormResponseStatus.Completed, response.Status);
        Assert.NotNull(response.CompletedAt);
        Assert.Equal(3u, response.RevisionNumber);
        Assert.Equal(2u, response.RowVersion);
    }

    private async Task AssertBoundResponseDraftAsync(Guid formResponseId)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        FormResponse response = await dbContext.FormResponses
            .AsNoTracking()
            .SingleAsync(item => item.Id == formResponseId)
            .ConfigureAwait(false);
        Assert.Equal(FormResponseStatus.Draft, response.Status);
        Assert.Null(response.CompletedAt);
        Assert.Equal(1u, response.RevisionNumber);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private HttpClient Client { get; }

    private HttpClient OtherClient { get; }

    private JsonApiClient Api { get; }

    private JsonApiWorkflow Workflow { get; }

    private sealed record DocumentFixture(
        Guid PatientId,
        Guid EncounterId,
        Guid FormDefinitionId,
        Guid FormVersionId,
        Guid DocumentDefinitionId);
}
