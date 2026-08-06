using System.Net;
using System.Text.Json;

using Cynara.Api.Tests.Support;
using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Patients;

namespace Cynara.Api.Tests.ClinicalRecord;

/// <summary>
/// CYN-57 authorization allow/deny matrix for the clinical record surface.
/// Runs against the real <see cref="Application.Modules.Capabilities.
/// EffectiveCapabilityResolver"/> with <c>grantAllCapabilities: false</c>
/// and covers read allow, read deny, write-with-read-only deny, no-actor
/// deny, cross-hospital deny, access.denied audit emission, and
/// no-state-change on denied writes.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ClinicalRecordAuthorizationMatrixTests : IAsyncDisposable
{
    private const string PrimaryHospitalCode =
        CynaraTenantWebApplicationFactory.PrimaryCode;

    private const string OtherHospitalCode =
        CynaraTenantWebApplicationFactory.OtherCode;

    private const string Admin = "matrix-admin";

    /// <summary>Actor that is never granted a capability; used for deny cases.</summary>
    private const string DenyActor = "matrix-deny";

    public ClinicalRecordAuthorizationMatrixTests(PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(
            database.Settings,
            grantAllCapabilities: false);
        HttpClient adminClient = CreateClient(Admin, PrimaryHospitalCode);
        Factory.EnsureBootstrapHospitalAsync().GetAwaiter().GetResult();
        Factory.SeedSecondaryHospitalAsync().GetAwaiter().GetResult();

        Workflow = new ClinicalRecordWorkflow(
            new JsonApiClient(adminClient), adminClient, Factory);
        foreach (string capability in CapabilityCodes.All)
        {
            Workflow.SeedCapabilityAsync(
                Admin, capability, PrimaryHospitalCode).GetAwaiter().GetResult();
        }

        Workspace = new Lazy<Task<ClinicalWorkspace>>(() =>
            Workflow.BuildWorkspaceAsync("matrix"));
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("patients", CapabilityCodes.PatientsRead)]
    [InlineData("encounters", CapabilityCodes.EncountersRead)]
    [InlineData("clinicalDocuments", CapabilityCodes.ClinicalDocumentsRead)]
    [InlineData("documentDefinitions", CapabilityCodes.CatalogRead)]
    [InlineData("facilities", CapabilityCodes.CatalogRead)]
    [InlineData("auditEvents", CapabilityCodes.AuditRead)]
    [InlineData("workspace", CapabilityCodes.WorkspaceRead)]
    public async Task ReadEndpoint_AllowsWithReadGrant(string resource, string capability)
    {
        string actor = $"matrix-read-{resource}";
        HttpClient client = CreateClient(actor, PrimaryHospitalCode);
        await Workflow.SeedCapabilityAsync(
            actor, capability, PrimaryHospitalCode).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri($"/api/{resource}", UriKind.Relative))
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("patients")]
    [InlineData("encounters")]
    [InlineData("clinicalDocuments")]
    [InlineData("documentDefinitions")]
    [InlineData("facilities")]
    [InlineData("auditEvents")]
    [InlineData("workspace")]
    public async Task ReadEndpoint_DeniesWithoutGrant(string resource)
    {
        HttpClient client = CreateClient(DenyActor, PrimaryHospitalCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri($"/api/{resource}", UriKind.Relative))
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        Assert.True(
            await Workflow.CountAuditEventsAsync("access.denied").ConfigureAwait(false) >= 1,
            "Denied read must emit an access.denied audit event.");
    }

    [Theory]
    [InlineData("patients")]
    [InlineData("encounters")]
    [InlineData("clinicalDocuments")]
    [InlineData("documentDefinitions")]
    [InlineData("facilities")]
    [InlineData("auditEvents")]
    [InlineData("workspace")]
    public async Task ReadEndpoint_DeniesWithoutActorIdentity(string resource)
    {
        HttpClient client = CreateClient(actorId: null, PrimaryHospitalCode);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri($"/api/{resource}", UriKind.Relative))
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
    }

    [Theory]
    [InlineData(WriteTargets.Patients)]
    [InlineData(WriteTargets.Facilities)]
    [InlineData(WriteTargets.Encounters)]
    [InlineData(WriteTargets.ClinicalDocuments)]
    [InlineData(WriteTargets.DocumentDefinitions)]
    [InlineData(WriteTargets.Workspace)]
    public async Task WriteEndpoint_AllowsWithWriteGrant(string target)
    {
        string actor = WriterFor(target);
        HttpClient client = CreateClient(actor, PrimaryHospitalCode);
        await Workflow.SeedCapabilityAsync(
            actor, ReadCapabilityFor(target), PrimaryHospitalCode).ConfigureAwait(false);
        await Workflow.SeedCapabilityAsync(
            actor, WriteCapabilityFor(target), PrimaryHospitalCode).ConfigureAwait(false);

        bool isWorkspace = string.Equals(
            target, WriteTargets.Workspace, StringComparison.Ordinal);
        HttpRequestMessage request = isWorkspace
            ? await BuildWorkspacePatchRequestAsync(client).ConfigureAwait(false)
            : await BuildWriteRequestAsync(target).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .SendAsync(request)
            .ConfigureAwait(false);

        Assert.Equal(
            isWorkspace ? HttpStatusCode.OK : HttpStatusCode.Created,
            response.StatusCode);
    }

    [Theory]
    [InlineData(WriteTargets.Patients)]
    [InlineData(WriteTargets.Facilities)]
    [InlineData(WriteTargets.Encounters)]
    [InlineData(WriteTargets.ClinicalDocuments)]
    [InlineData(WriteTargets.DocumentDefinitions)]
    [InlineData(WriteTargets.Workspace)]
    public async Task WriteEndpoint_DeniesReadOnlyActor(string target)
    {
        string actor = ReadOnlyFor(target);
        HttpClient client = CreateClient(actor, PrimaryHospitalCode);
        await Workflow.SeedCapabilityAsync(
            actor, ReadCapabilityFor(target), PrimaryHospitalCode).ConfigureAwait(false);
        int deniedBefore = await Workflow.CountAuditEventsAsync("access.denied")
            .ConfigureAwait(false);

        HttpRequestMessage request = await BuildWriteRequestAsync(target)
            .ConfigureAwait(false);
        using HttpResponseMessage response = await client
            .SendAsync(request)
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        Assert.True(
            await Workflow.CountAuditEventsAsync("access.denied").ConfigureAwait(false)
                > deniedBefore,
            "Denied write must emit an access.denied audit event.");
    }

    [Theory]
    [InlineData(WriteTargets.Patients)]
    [InlineData(WriteTargets.Facilities)]
    [InlineData(WriteTargets.Encounters)]
    [InlineData(WriteTargets.ClinicalDocuments)]
    [InlineData(WriteTargets.DocumentDefinitions)]
    [InlineData(WriteTargets.Workspace)]
    public async Task WriteEndpoint_DeniesWithoutActorIdentity(string target)
    {
        HttpClient client = CreateClient(actorId: null, PrimaryHospitalCode);

        HttpRequestMessage request = await BuildWriteRequestAsync(target)
            .ConfigureAwait(false);
        using HttpResponseMessage response = await client
            .SendAsync(request)
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
    }

    [Theory]
    [InlineData(WriteTargets.Patients)]
    [InlineData(WriteTargets.Facilities)]
    [InlineData(WriteTargets.Encounters)]
    [InlineData(WriteTargets.ClinicalDocuments)]
    [InlineData(WriteTargets.DocumentDefinitions)]
    [InlineData(WriteTargets.Workspace)]
    public async Task WriteEndpoint_CrossHospitalGrantDoesNotAuthorize(string target)
    {
        string actor = WriterFor(target);
        await Workflow.SeedCapabilityAsync(
            actor, WriteCapabilityFor(target), PrimaryHospitalCode).ConfigureAwait(false);
        HttpClient otherClient = CreateClient(actor, OtherHospitalCode);

        HttpRequestMessage request = await BuildWriteRequestAsync(target)
            .ConfigureAwait(false);
        using HttpResponseMessage response = await otherClient
            .SendAsync(request)
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
    }

    [Theory]
    [InlineData(WriteTargets.Patients)]
    [InlineData(WriteTargets.Facilities)]
    [InlineData(WriteTargets.Encounters)]
    [InlineData(WriteTargets.ClinicalDocuments)]
    [InlineData(WriteTargets.DocumentDefinitions)]
    public async Task DeniedWrite_LeavesNoStateChange(string target)
    {
        string actor = ReadOnlyFor(target);
        HttpClient client = CreateClient(actor, PrimaryHospitalCode);
        await Workflow.SeedCapabilityAsync(
            actor, ReadCapabilityFor(target), PrimaryHospitalCode).ConfigureAwait(false);
        HttpRequestMessage request = await BuildWriteRequestAsync(target)
            .ConfigureAwait(false);
        int before = await CountTargetRowsAsync(target).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .SendAsync(request)
            .ConfigureAwait(false);

        await AssertForbiddenAsync(response).ConfigureAwait(false);
        Assert.Equal(
            before,
            await CountTargetRowsAsync(target).ConfigureAwait(false));
    }

    private HttpClient CreateClient(string? actorId, string hospitalCode)
    {
        HttpClient client = Factory.CreateClient();
        client.AcceptJsonApi();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Hospital-Code", hospitalCode);
        if (actorId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Actor-Id", actorId);
        }

        return client;
    }

    private async Task<ClinicalWorkspace> WorkspaceAsync()
    {
        return await Workspace.Value.ConfigureAwait(false);
    }

    private Task<HttpRequestMessage> BuildWriteRequestAsync(string target)
    {
        return target switch
        {
            WriteTargets.Patients => Task.FromResult(JsonRequest(
                HttpMethod.Post,
                "/api/patients",
                new
                {
                    mrn = "MRN-MATRIX-PATIENT",
                    givenName = "Ada",
                    familyName = "Lovelace",
                    birthDate = "1990-01-01",
                    sex = "female",
                })),
            WriteTargets.Facilities => Task.FromResult(JsonRequest(
                HttpMethod.Post,
                "/api/facilities",
                new { code = "mx-facility", name = "Matrix facility" })),
            _ => BuildReferenceWriteRequestAsync(target),
        };
    }

    private async Task<HttpRequestMessage> BuildReferenceWriteRequestAsync(
        string target)
    {
        ClinicalWorkspace workspace = await WorkspaceAsync().ConfigureAwait(false);
        return target switch
        {
            WriteTargets.Encounters => JsonRequest(
                HttpMethod.Post,
                "/api/encounters",
                new
                {
                    patientId = workspace.PatientId,
                    facilityId = workspace.FacilityId,
                    clinicalAreaId = workspace.ClinicalAreaId,
                    type = "ambulatory",
                    responsibleProfessionalId = "dr-who",
                }),
            WriteTargets.ClinicalDocuments => JsonRequest(
                HttpMethod.Post,
                "/api/clinicalDocuments",
                new
                {
                    documentDefinitionId = workspace.DocumentDefinitionId,
                    encounterId = workspace.EncounterId,
                }),
            WriteTargets.DocumentDefinitions => BuildDocumentDefinitionCreateRequest(
                workspace),
            WriteTargets.Workspace => JsonRequest(
                HttpMethod.Patch,
                "/api/workspace",
                new
                {
                    name = "Matrix renamed workspace",
                    metadataJson = "{}",
                    rowVersion = 0U,
                }),
            _ => throw new InvalidOperationException(
                $"{target} does not need workspace references."),
        };
    }

    private static async Task<HttpRequestMessage> BuildWorkspacePatchRequestAsync(
        HttpClient client)
    {
        using HttpResponseMessage get = await client
            .GetAsync(new Uri("/api/workspace", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var workspace = JsonDocument.Parse(
            await get.Content.ReadAsStringAsync().ConfigureAwait(false));
        var rowVersion = workspace.RootElement.GetProperty("rowVersion").GetUInt32();
        return JsonRequest(
            HttpMethod.Patch,
            "/api/workspace",
            new
            {
                name = "Matrix renamed workspace",
                metadataJson = "{}",
                rowVersion,
            });
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
                    code = "mx-def",
                    name = "Matrix document",
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

    private async Task<int> CountTargetRowsAsync(string target)
    {
        return target switch
        {
            WriteTargets.Patients => await Workflow.CountAsync<Patient>()
                .ConfigureAwait(false),
            WriteTargets.Facilities => await Workflow.CountAsync<Facility>()
                .ConfigureAwait(false),
            WriteTargets.Encounters => await Workflow.CountAsync<Encounter>()
                .ConfigureAwait(false),
            WriteTargets.ClinicalDocuments => await Workflow.CountAsync<ClinicalDocument>()
                .ConfigureAwait(false),
            WriteTargets.DocumentDefinitions => await Workflow
                .CountAsync<DocumentDefinition>().ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        string message = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Expected 403, got {(int)response.StatusCode}: {body}");
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            message);
        using var document = JsonDocument.Parse(body);
        JsonElement error = Assert.Single(
            document.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("403", error.GetProperty("status").GetString());
        Assert.Equal(
            "Capability required",
            error.GetProperty("title").GetString());
    }

    private static HttpRequestMessage JsonRequest(
        HttpMethod method,
        string path,
        object body)
    {
        return new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                ClinicalRecordWorkflow.ContentType),
        };
    }

    private static string ReadOnlyFor(string target)
    {
        return $"matrix-readonly-{target}";
    }

    private static string WriterFor(string target)
    {
        return $"matrix-writer-{target}";
    }

    private static string ReadCapabilityFor(string target)
    {
        return target switch
        {
            WriteTargets.Patients => CapabilityCodes.PatientsRead,
            WriteTargets.Facilities => CapabilityCodes.CatalogRead,
            WriteTargets.Encounters => CapabilityCodes.EncountersRead,
            WriteTargets.ClinicalDocuments => CapabilityCodes.ClinicalDocumentsRead,
            WriteTargets.DocumentDefinitions => CapabilityCodes.CatalogRead,
            WriteTargets.Workspace => CapabilityCodes.WorkspaceRead,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    private static string WriteCapabilityFor(string target)
    {
        return target switch
        {
            WriteTargets.Patients => CapabilityCodes.PatientsWrite,
            WriteTargets.Facilities => CapabilityCodes.CatalogWrite,
            WriteTargets.Encounters => CapabilityCodes.EncountersWrite,
            WriteTargets.ClinicalDocuments => CapabilityCodes.ClinicalDocumentsWrite,
            WriteTargets.DocumentDefinitions => CapabilityCodes.CatalogWrite,
            WriteTargets.Workspace => CapabilityCodes.WorkspaceWrite,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private ClinicalRecordWorkflow Workflow { get; }

    private Lazy<Task<ClinicalWorkspace>> Workspace { get; }

    private static class WriteTargets
    {
        public const string Patients = "patients";

        public const string Facilities = "facilities";

        public const string Encounters = "encounters";

        public const string ClinicalDocuments = "clinicalDocuments";

        public const string DocumentDefinitions = "documentDefinitions";

        public const string Workspace = "workspace";
    }
}
