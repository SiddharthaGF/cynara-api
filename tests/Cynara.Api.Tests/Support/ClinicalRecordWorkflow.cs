using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Cynara.Domain.Audit;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Shared arrange/assert helpers for the CYN-57 clinical-record lifecycle
/// suites. Seeds the taxonomy → form → document-definition → patient →
/// encounter chain, drives document transitions, and asserts audit events,
/// capability assignments, and row counts against a
/// <see cref="CynaraTenantWebApplicationFactory"/>.
/// </summary>
internal sealed class ClinicalRecordWorkflow(
    JsonApiClient api,
    HttpClient client,
    CynaraTenantWebApplicationFactory factory)
{
    private readonly JsonApiWorkflow workflow = new(api, client);

    public JsonApiClient Api { get; } = api;

    public HttpClient Client { get; } = client;

    public CynaraTenantWebApplicationFactory Factory { get; } = factory;

    public async Task<ClinicalWorkspace> BuildWorkspaceAsync(
        string suffix,
        bool allowsMultipleInstancesPerEncounter = false,
        string? clinicalSchemaJson = null,
        string? rulesSchemaJson = null,
        string? fieldCode = null)
    {
        Guid facilityId = await CreateFacilityAsync(
            $"cr-fac-{suffix}", $"Facility {suffix}").ConfigureAwait(false);
        Guid clinicalAreaId = await CreateClinicalAreaAsync(
            $"cr-area-{suffix}", $"Area {suffix}", facilityId).ConfigureAwait(false);
        Guid disciplineId = await CreateDisciplineAsync(
            $"cr-disc-{suffix}", $"Discipline {suffix}", clinicalAreaId).ConfigureAwait(false);

        (string formDefinitionId, string formVersionId) = await workflow
            .PublishFormAsync(
                $"cr-form-{suffix}",
                $"Form {suffix}",
                clinicalSchemaJson ?? JsonApiWorkflow.MinimalClinicalSchema(
                    $"{suffix}-field",
                    fieldCode ?? $"cr.{suffix}"),
                rulesSchemaJson: rulesSchemaJson)
            .ConfigureAwait(false);

        string documentDefinitionCode = $"cr-def-{suffix}";
        Guid documentDefinitionId = await CreateDocumentDefinitionAsync(
            documentDefinitionCode,
            $"Document {suffix}",
            Guid.Parse(formDefinitionId),
            Guid.Parse(formVersionId),
            facilityId,
            clinicalAreaId,
            disciplineId,
            allowsMultipleInstancesPerEncounter).ConfigureAwait(false);
        Guid patientId = await CreatePatientAsync(
            $"MRN-{suffix}",
            "Ada",
            "Lovelace",
            "1990-01-01",
            "female").ConfigureAwait(false);
        Guid encounterId = await CreateEncounterAsync(
            patientId,
            facilityId,
            clinicalAreaId,
            "ambulatory",
            "dr-who").ConfigureAwait(false);

        return new ClinicalWorkspace(
            facilityId,
            clinicalAreaId,
            disciplineId,
            Guid.Parse(formDefinitionId),
            Guid.Parse(formVersionId),
            documentDefinitionId,
            documentDefinitionCode,
            patientId,
            encounterId);
    }

    public Task<Guid> CreateFacilityAsync(string code, string name)
    {
        return CreatePlainAsync("facilities", new { code, name });
    }

    public Task<Guid> CreateClinicalAreaAsync(string code, string name, Guid facilityId)
    {
        return CreatePlainAsync("clinicalAreas", new { code, name, facilityId });
    }

    public Task<Guid> CreateDisciplineAsync(string code, string name, Guid clinicalAreaId)
    {
        return CreatePlainAsync("disciplines", new { code, name, clinicalAreaId });
    }

    public Task<Guid> CreatePatientAsync(
        string mrn,
        string givenName,
        string familyName,
        string birthDate,
        string sex)
    {
        return CreatePlainAsync(
            "patients",
            new { mrn, givenName, familyName, birthDate, sex, bloodType = "o+" });
    }

    public Task<Guid> CreateEncounterAsync(
        Guid patientId,
        Guid facilityId,
        Guid clinicalAreaId,
        string type,
        string responsibleProfessionalId)
    {
        return CreatePlainAsync(
            "encounters",
            new
            {
                patientId,
                facilityId,
                clinicalAreaId,
                type,
                responsibleProfessionalId,
            });
    }

    public async Task<Guid> CreateDocumentDefinitionAsync(
        string code,
        string name,
        Guid formDefinitionId,
        Guid formVersionId,
        Guid facilityId,
        Guid clinicalAreaId,
        Guid disciplineId,
        bool allowsMultipleInstancesPerEncounter = false)
    {
        using JsonDocument created = await Api.PostResourceAsync(
            "documentDefinitions",
            new
            {
                code,
                name,
                allowsMultipleInstancesPerEncounter,
                requiresActorForCreation = true,
                requiresActorForCompletion = true,
            },
            new
            {
                formDefinition = new
                {
                    data = new { type = "formDefinitions", id = formDefinitionId },
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
        return Guid.Parse(JsonApiClient.RequireId(created));
    }

    public Task<JsonDocument> StartDocumentAsync(
        Guid documentDefinitionId,
        Guid encounterId)
    {
        return SendAndReadAsync(
            PostJsonRequest(
                "/api/clinicalDocuments",
                new { documentDefinitionId, encounterId }),
            "Start clinical document");
    }

    public Task<HttpResponseMessage> SendStartDocumentAsync(
        Guid documentDefinitionId,
        Guid encounterId)
    {
        return Client.SendAsync(PostJsonRequest(
            "/api/clinicalDocuments",
            new { documentDefinitionId, encounterId }));
    }

    public Task<JsonDocument> CompleteDocumentAsync(Guid documentId, uint rowVersion)
    {
        return SendAndReadAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/complete",
                new { rowVersion }),
            "Complete clinical document");
    }

    public Task<HttpResponseMessage> SendCompleteDocumentAsync(
        Guid documentId,
        uint rowVersion)
    {
        return Client.SendAsync(PostJsonRequest(
            $"/api/clinicalDocuments/{documentId}/complete",
            new { rowVersion }));
    }

    public Task<JsonDocument> CancelDocumentAsync(Guid documentId, uint rowVersion)
    {
        return SendAndReadAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/cancel",
                new { rowVersion }),
            "Cancel clinical document");
    }

    public Task<JsonDocument> EnterInErrorAsync(
        Guid documentId,
        uint rowVersion,
        string reason)
    {
        return SendAndReadAsync(
            PostJsonRequest(
                $"/api/clinicalDocuments/{documentId}/enter-in-error",
                new { rowVersion, reason }),
            "Enter clinical document in error");
    }

    public Task<HttpResponseMessage> PatchBoundResponseAsync(
        Guid formResponseId,
        string answersJson,
        uint rowVersion)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri($"/api/formResponses/{formResponseId}", UriKind.Relative))
        {
            Content = JsonApiClient.CreateJsonApiContent(new
            {
                data = new
                {
                    type = "formResponses",
                    id = formResponseId,
                    attributes = new { answersJson, rowVersion },
                },
            }),
        };
        return Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> SoftDeleteResponseAsync(Guid formResponseId)
    {
        return Api.DeleteAsync($"/api/formResponses/{formResponseId}");
    }

    public Task<JsonDocument> GetDocumentAsync(Guid documentId)
    {
        return Api.GetAsync($"/api/clinicalDocuments/{documentId}");
    }

    public Task<string> GetFormResponseAnswersAsync(Guid formResponseId)
    {
        return GetFormResponseAttributeAsync(formResponseId, "answersJson");
    }

    public async Task<string> GetFormResponseAttributeAsync(
        Guid formResponseId,
        string attribute)
    {
        using JsonDocument document = await Api
            .GetAsync($"/api/formResponses/{formResponseId}")
            .ConfigureAwait(false);
        return JsonApiClient.AttrString(document, attribute) ?? string.Empty;
    }

    public Task<JsonDocument> SearchPatientsAsync(string mrn)
    {
        return Api.GetAsync(
            $"/api/patients?mrn={Uri.EscapeDataString(mrn)}");
    }

    public async Task<JsonDocument> PublishNextFormVersionAsync(
        Guid formDefinitionId,
        string clinicalSchemaJson)
    {
        using HttpResponseMessage createDraft = await Client.PostAsync(
            new Uri(
                $"/api/formDefinitions/{formDefinitionId}/create-draft",
                UriKind.Relative),
            content: null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createDraft.StatusCode);

        string draftId = await workflow
            .GetFormDraftIdAsync(formDefinitionId.ToString())
            .ConfigureAwait(false);
        using JsonDocument draft = await workflow
            .GetVersionAsync("formVersions", draftId)
            .ConfigureAwait(false);
        using JsonDocument updated = await Api.PatchResourceAsync(
            "formVersions",
            draftId,
            new
            {
                clinicalSchemaJson,
                uiSchemaJson = JsonApiClient.AttrString(draft, "uiSchemaJson"),
                rulesSchemaJson = JsonApiClient.AttrString(draft, "rulesSchemaJson"),
                rowVersion = JsonApiClient.AttrUInt(draft, "rowVersion"),
            }).ConfigureAwait(false);
        return await workflow.SubmitAndPublishFormAsync(draftId)
            .ConfigureAwait(false);
    }

    public async Task AssertAuditAsync(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId)
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
        Assert.Equal(actorId, auditEvent.ActorId);
    }

    public async Task<int> CountAuditEventsAsync(string action)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.AuditEvents
            .AsNoTracking()
            .CountAsync(item => item.Action == action)
            .ConfigureAwait(false);
    }

    public async Task SeedCapabilityAsync(
        string actorId,
        string capability,
        string hospitalCode)
    {
        await using CynaraTenantWebApplicationFactory.FactoryScope scope =
            Factory.CreateScope();
        Hospital hospital = await scope
            .LoadHospitalAsync(hospitalCode)
            .ConfigureAwait(false);
        bool exists = await scope.DbContext.CapabilityAssignments
            .AsNoTracking()
            .AnyAsync(item =>
                item.HospitalId == hospital.Id
                && item.ActorId == actorId
                && item.Capability == capability)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        scope.DbContext.CapabilityAssignments.Add(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospital.Id,
            ActorId = actorId,
            Capability = capability,
            AssignedAt = DateTimeOffset.UtcNow,
        });
        _ = await scope.DbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    public Task<int> CountAsync<TEntity>()
        where TEntity : class
    {
        return CountAsync(static dbContext => dbContext.Set<TEntity>());
    }

    public async Task<int> CountAsync(Func<CynaraDbContext, IQueryable<object>> selector)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await selector(dbContext).CountAsync().ConfigureAwait(false);
    }

    public async Task<bool> ClinicalDocumentExistsAsync(Guid id)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        return await dbContext.ClinicalDocuments
            .AsNoTracking()
            .AnyAsync(item => item.Id == id)
            .ConfigureAwait(false);
    }

    public static string GetString(JsonDocument document, string name)
    {
        return GetString(document.RootElement, name);
    }

    public static string GetString(JsonElement element, string name)
    {
        return element.GetProperty(name).GetString() ?? string.Empty;
    }

    public static Guid RequireRootId(JsonDocument document)
    {
        string id = document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Missing root id");
        return Guid.Parse(id);
    }

    public static HttpRequestMessage PostJsonRequest(string path, object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue(ContentType),
                },
            },
        };
    }

    private async Task<Guid> CreatePlainAsync(string path, object body)
    {
        using JsonDocument document = await PostRawAsync(path, body)
            .ConfigureAwait(false);
        return RequireRootId(document);
    }

    private async Task<JsonDocument> PostRawAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PostAsync(
            new Uri($"/api/{path}", UriKind.Relative),
            new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                ContentType)).ConfigureAwait(false);
        return await ReadSuccessAsync(
            response, $"POST /api/{path}").ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendAndReadAsync(
        HttpRequestMessage request,
        string operation)
    {
        using HttpResponseMessage response = await Client
            .SendAsync(request)
            .ConfigureAwait(false);
        return await ReadSuccessAsync(response, operation).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadSuccessAsync(
        HttpResponseMessage response,
        string operation)
    {
        string text = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        if ((int)response.StatusCode is < 200 or >= 300)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{operation} failed with "
                    + $"{(int)response.StatusCode}: {text}"));
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    public const string ContentType = "application/vnd.api+json";

    public const string BpClinicalSchemaJson = /*lang=json,strict*/ """
        {
          "schemaVersion": "1.0.0",
          "fields": [
            { "id": "systolic", "code": "vital.bp.systolic", "type": "integer" },
            { "id": "diastolic", "code": "vital.bp.diastolic", "type": "integer" }
          ]
        }
        """;

    public const string BpValidationRulesJson = /*lang=json,strict*/ """
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

/// <summary>
/// The seeded clinical record fixture produced by
/// <see cref="ClinicalRecordWorkflow.BuildWorkspaceAsync"/>.
/// </summary>
internal sealed record ClinicalWorkspace(
    Guid FacilityId,
    Guid ClinicalAreaId,
    Guid DisciplineId,
    Guid FormDefinitionId,
    Guid FormVersionId,
    Guid DocumentDefinitionId,
    string DocumentDefinitionCode,
    Guid PatientId,
    Guid EncounterId);
