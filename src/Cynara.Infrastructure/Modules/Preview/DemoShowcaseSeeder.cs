using System.Text.Json;

using Cynara.Application;
using Cynara.Application.Components;
using Cynara.Application.Failures;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.ClinicalTaxonomy;
using Cynara.Application.Modules.Components;
using Cynara.Application.Modules.Documents;
using Cynara.Application.Modules.Encounters;
using Cynara.Application.Modules.FormAi;
using Cynara.Application.Modules.FormResponses;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Patients;
using Cynara.Application.Modules.Workflows;
using Cynara.Application.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Preview;

public static class DemoShowcaseSeeder
{
    public const string ComponentCode = "patient-demographics";
    public const string FormCode = "demo-showcase";
    public const string WorkflowCode = "patient-triage";

    private const string ActorId = "designer-user";

    private const string FacilityCode = "fac-main";
    private const string ClinicalAreaCode = "area-emergency";
    private const string DisciplineCode = "disc-nursing";
    private const string DocumentDefinitionCode = "doc-intake";

    private const string PatientTriageWorkflowSchema =
        /*lang=json,strict*/ """
        {
          "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
          "schemaVersion": "1.0.0",
          "inputs": ["assessment.pain-score"],
          "nodes": [
            { "id": "start", "type": "start", "name": "Triaje iniciado" },
            { "id": "triage", "type": "decision", "name": "Valoración del dolor" },
            {
              "id": "intake-task",
              "type": "task",
              "name": "Evaluación de ingreso",
              "description": "Completar la evaluación de ingreso del paciente.",
              "formCode": "demo-showcase",
              "formVersion": "1.0.0",
              "assignee": { "role": "nurse" },
              "dueDays": 1
            },
            {
              "id": "high-task",
              "type": "task",
              "name": "Revisión médica urgente",
              "description": "Revisión médica para dolor severo.",
              "formCode": "demo-showcase",
              "formVersion": "1.0.0",
              "assignee": { "role": "physician" },
              "dueDays": 2
            },
            { "id": "end", "type": "end", "name": "Completado" }
          ],
          "edges": [
            { "from": "start", "to": "triage" },
            {
              "from": "triage",
              "to": "high-task",
              "condition": {
                "op": "gte",
                "args": [ { "ref": "assessment.pain-score" }, { "lit": 7 } ]
              }
            },
            { "from": "triage", "to": "intake-task", "label": "Dolor leve o moderado" },
            { "from": "intake-task", "to": "end" },
            { "from": "high-task", "to": "end" }
          ]
        }
        """;

    public static async Task SeedDemoShowcaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            await SeedWorkspaceCoreAsync(
                scope.ServiceProvider,
                seedClinicalData: false,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Seeds the whole demo workspace: hospital, capabilities, component,
    /// published form, published workflow, clinical taxonomy, patients,
    /// encounters, document catalog, clinical documents, form responses, AI
    /// provider settings, and a sample failure log.
    /// </summary>
    public static async Task SeedFullDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            await SeedWorkspaceCoreAsync(
                scope.ServiceProvider,
                seedClinicalData: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static Task SeedPreviewDemoAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return services.SeedFullDatabaseAsync(cancellationToken);
    }

    private static async Task SeedWorkspaceCoreAsync(
        IServiceProvider services,
        bool seedClinicalData,
        CancellationToken cancellationToken)
    {
        CynaraDbContext dbContext = services
            .GetRequiredService<CynaraDbContext>();
        Hospital hospital = await ResolveHospitalAsync(
                dbContext,
                cancellationToken)
            .ConfigureAwait(false);
        HospitalContext hospitalContext = services
            .GetRequiredService<HospitalContext>();
        hospitalContext.SetWorkspace(hospital.Id, hospital.Code, hospital.Name);

        CurrentActorOverride actorOverride = services
            .GetRequiredService<CurrentActorOverride>();
        actorOverride.ActorId = ActorId;

        ICapabilityAssignmentService capabilities = services
            .GetRequiredService<ICapabilityAssignmentService>();
        await EnsureCapabilitiesAsync(capabilities, cancellationToken)
            .ConfigureAwait(false);

        IComponentQueryService componentQueries = services
            .GetRequiredService<IComponentQueryService>();
        IComponentLifecycleService componentLifecycle = services
            .GetRequiredService<IComponentLifecycleService>();
        await EnsureComponentAsync(
                componentQueries,
                componentLifecycle,
                cancellationToken)
            .ConfigureAwait(false);

        IFormService forms = services.GetRequiredService<IFormService>();
        await UpsertFormAsync(forms, cancellationToken).ConfigureAwait(false);
        await EnsurePublishedFormAsync(
                forms,
                services,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureWorkflowsAsync(services, cancellationToken)
            .ConfigureAwait(false);

        if (seedClinicalData)
        {
            await SeedClinicalDataAsync(
                    services,
                    hospital.Id,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task SeedClinicalDataAsync(
        IServiceProvider services,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        IClinicalTaxonomyService taxonomy = services
            .GetRequiredService<IClinicalTaxonomyService>();
        FacilityDto facility = await EnsureFacilityAsync(
                taxonomy,
                cancellationToken)
            .ConfigureAwait(false);
        ClinicalAreaDto area = await EnsureClinicalAreaAsync(
                taxonomy,
                facility.Id,
                cancellationToken)
            .ConfigureAwait(false);
        DisciplineDto discipline = await EnsureDisciplineAsync(
                taxonomy,
                area.Id,
                cancellationToken)
            .ConfigureAwait(false);

        IPatientService patients = services
            .GetRequiredService<IPatientService>();
        PatientDto patientA = await EnsurePatientAsync(
                patients,
                "P-10001",
                "44889911",
                "María",
                "González",
                new DateOnly(1985, 3, 14),
                "female",
                cancellationToken)
            .ConfigureAwait(false);
        PatientDto patientB = await EnsurePatientAsync(
                patients,
                "P-10002",
                "55667788",
                "Juan",
                "Pérez",
                new DateOnly(1990, 11, 2),
                "male",
                cancellationToken)
            .ConfigureAwait(false);

        IEncounterService encounters = services
            .GetRequiredService<IEncounterService>();
        EncounterDto openEncounter = await EnsureEncounterAsync(
                encounters,
                patientA.Id,
                facility.Id,
                area.Id,
                "ambulatory",
                "dr-castro",
                cancellationToken)
            .ConfigureAwait(false);
        EncounterDto workEncounter = await EnsureEncounterAsync(
                encounters,
                patientA.Id,
                facility.Id,
                area.Id,
                "observation",
                "dr-castro",
                cancellationToken)
            .ConfigureAwait(false);
        _ = await EnsureCompletedEncounterAsync(
            encounters,
            patientB.Id,
            facility.Id,
            area.Id,
            "emergency",
            "dr-lopez",
            cancellationToken).ConfigureAwait(false);

        IFormService forms = services.GetRequiredService<IFormService>();
        FormSummaryDto summary = await forms
            .GetSummaryAsync(FormCode, cancellationToken)
            .ConfigureAwait(false);
        string publishedVersion = summary.PublishedVersions[^1];
        FormVersionDto published = await forms
            .GetVersionAsync(FormCode, publishedVersion, cancellationToken)
            .ConfigureAwait(false);

        IDocumentCatalogService catalog = services
            .GetRequiredService<IDocumentCatalogService>();
        DocumentDefinitionDto definition = await EnsureDocumentDefinitionAsync(
                catalog,
                published.Id,
                facility.Id,
                area.Id,
                discipline.Id,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureClinicalDocumentsAsync(
                services,
                definition.Id,
                openEncounter,
                workEncounter,
                cancellationToken)
            .ConfigureAwait(false);

        CynaraDbContext dbContext = services
            .GetRequiredService<CynaraDbContext>();
        await EnsureStandaloneResponseAsync(
                services,
                dbContext,
                hospitalId,
                published.Id,
                publishedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureAiSettingsAsync(services, cancellationToken)
            .ConfigureAwait(false);

        IFailureLogWriter failureLogs = services
            .GetRequiredService<IFailureLogWriter>();
        await EnsureFailureLogAsync(
                dbContext,
                failureLogs,
                hospitalId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureClinicalDocumentsAsync(
        IServiceProvider services,
        Guid definitionId,
        EncounterDto openEncounter,
        EncounterDto workEncounter,
        CancellationToken cancellationToken)
    {
        IClinicalDocumentService documents = services
            .GetRequiredService<IClinicalDocumentService>();
        IFormResponseLifecycleService responses = services
            .GetRequiredService<IFormResponseLifecycleService>();
        await EnsureCompletedDocumentAsync(
                documents,
                responses,
                definitionId,
                openEncounter,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureInProgressDocumentAsync(
                documents,
                responses,
                definitionId,
                workEncounter,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureAiSettingsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        IAiProviderSettingsService aiSettings = services
            .GetRequiredService<IAiProviderSettingsService>();
        _ = await aiSettings.UpsertAsync(
            new FormAiSettingsUpdateRequest(
                ClearApiKey: true,
                BaseUrl: "https://api.openai.com/v1",
                Model: "gpt-4o-mini"),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FacilityDto> EnsureFacilityAsync(
        IClinicalTaxonomyService taxonomy,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FacilityDto> existing = await taxonomy
            .ListFacilitiesAsync(includeRetired: true, cancellationToken)
            .ConfigureAwait(false);
        FacilityDto? match = existing.FirstOrDefault(item =>
            string.Equals(item.Code, FacilityCode, StringComparison.Ordinal));
        return match ?? await taxonomy.CreateFacilityAsync(
            new CreateFacilityRequest(FacilityCode, "Campus principal"),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ClinicalAreaDto> EnsureClinicalAreaAsync(
        IClinicalTaxonomyService taxonomy,
        Guid facilityId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicalAreaDto> existing = await taxonomy
            .ListClinicalAreasAsync(
                facilityId,
                includeRetired: true,
                cancellationToken)
            .ConfigureAwait(false);
        ClinicalAreaDto? match = existing.FirstOrDefault(item =>
            string.Equals(item.Code, ClinicalAreaCode, StringComparison.Ordinal));
        return match ?? await taxonomy.CreateClinicalAreaAsync(
            new CreateClinicalAreaRequest(
                ClinicalAreaCode,
                "Urgencias",
                facilityId),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DisciplineDto> EnsureDisciplineAsync(
        IClinicalTaxonomyService taxonomy,
        Guid clinicalAreaId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DisciplineDto> existing = await taxonomy
            .ListDisciplinesAsync(
                clinicalAreaId,
                includeRetired: true,
                cancellationToken)
            .ConfigureAwait(false);
        DisciplineDto? match = existing.FirstOrDefault(item =>
            string.Equals(item.Code, DisciplineCode, StringComparison.Ordinal));
        return match ?? await taxonomy.CreateDisciplineAsync(
            new CreateDisciplineRequest(
                DisciplineCode,
                "Enfermería",
                clinicalAreaId),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PatientDto> EnsurePatientAsync(
        IPatientService patients,
        string mrn,
        string? nationalId,
        string givenName,
        string familyName,
        DateOnly birthDate,
        string sex,
        CancellationToken cancellationToken)
    {
        PatientListResponse existing = await patients
            .SearchAsync(
                new PatientSearchRequest(
                    Mrn: mrn,
                    NationalId: null,
                    GivenName: null,
                    FamilyName: null,
                    PageSize: 1),
                cancellationToken)
            .ConfigureAwait(false);
        PatientDto? match = existing.Patients.Count > 0
            ? existing.Patients[0]
            : null;
        return match ?? await patients.CreateAsync(
            new CreatePatientRequest(
                mrn,
                nationalId,
                givenName,
                familyName,
                birthDate,
                sex),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<EncounterDto> EnsureEncounterAsync(
        IEncounterService encounters,
        Guid patientId,
        Guid facilityId,
        Guid clinicalAreaId,
        string type,
        string responsibleProfessionalId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EncounterDto> existing = await encounters
            .ListAsync(
                new EncounterListRequest(PatientId: patientId),
                cancellationToken)
            .ConfigureAwait(false);
        EncounterDto? match = existing.FirstOrDefault(item =>
            item.FacilityId == facilityId
            && item.ClinicalAreaId == clinicalAreaId
            && string.Equals(item.Type, type, StringComparison.Ordinal));
        return match ?? await encounters.CreateAsync(
            new CreateEncounterRequest(
                patientId,
                facilityId,
                clinicalAreaId,
                type,
                responsibleProfessionalId),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<EncounterDto> EnsureCompletedEncounterAsync(
        IEncounterService encounters,
        Guid patientId,
        Guid facilityId,
        Guid clinicalAreaId,
        string type,
        string responsibleProfessionalId,
        CancellationToken cancellationToken)
    {
        EncounterDto encounter = await EnsureEncounterAsync(
                encounters,
                patientId,
                facilityId,
                clinicalAreaId,
                type,
                responsibleProfessionalId,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(encounter.Status, "completed", StringComparison.Ordinal))
        {
            return encounter;
        }

        return await encounters.CompleteAsync(
            encounter.Id,
            new TransitionEncounterRequest(encounter.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DocumentDefinitionDto> EnsureDocumentDefinitionAsync(
        IDocumentCatalogService catalog,
        Guid formVersionId,
        Guid facilityId,
        Guid clinicalAreaId,
        Guid disciplineId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentDefinitionDto> existing = await catalog
            .ListAsync(includeRetired: true, cancellationToken)
            .ConfigureAwait(false);
        DocumentDefinitionDto? match = existing.FirstOrDefault(item =>
            string.Equals(item.Code, DocumentDefinitionCode, StringComparison.Ordinal));
        return match ?? await catalog.CreateAsync(
            new CreateDocumentDefinitionRequest(
                DocumentDefinitionCode,
                "Evaluación de ingreso",
                formVersionId,
                facilityId,
                clinicalAreaId,
                disciplineId,
                AllowsMultipleInstancesPerEncounter: true,
                RequiresActorForCreation: true,
                RequiresActorForCompletion: true),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureCompletedDocumentAsync(
        IClinicalDocumentService documents,
        IFormResponseLifecycleService responses,
        Guid definitionId,
        EncounterDto encounter,
        CancellationToken cancellationToken)
    {
        if (await HasDocumentAsync(
                documents,
                definitionId,
                encounter.Id,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        ClinicalDocumentDto started = await documents.StartAsync(
            new StartClinicalDocumentRequest(definitionId, encounter.Id),
            ActorId,
            cancellationToken).ConfigureAwait(false);
        _ = await responses.UpdateAsync(
            started.FormResponseId,
            new UpdateFormResponseRequest(
                LoadJson("demo-answers-completed.json"),
                RowVersion: 0),
            ActorId,
            cancellationToken).ConfigureAwait(false);
        _ = await documents.CompleteAsync(
            started.Id,
            new TransitionClinicalDocumentRequest(started.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureInProgressDocumentAsync(
        IClinicalDocumentService documents,
        IFormResponseLifecycleService responses,
        Guid definitionId,
        EncounterDto encounter,
        CancellationToken cancellationToken)
    {
        if (await HasDocumentAsync(
                documents,
                definitionId,
                encounter.Id,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        ClinicalDocumentDto started = await documents.StartAsync(
            new StartClinicalDocumentRequest(definitionId, encounter.Id),
            ActorId,
            cancellationToken).ConfigureAwait(false);
        _ = await responses.UpdateAsync(
            started.FormResponseId,
            new UpdateFormResponseRequest(
                LoadJson("demo-answers-draft.json"),
                RowVersion: 0),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasDocumentAsync(
        IClinicalDocumentService documents,
        Guid definitionId,
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicalDocumentDto> existing = await documents
            .ListAsync(
                new ClinicalDocumentListRequest(
                    EncounterId: encounterId,
                    DocumentDefinitionId: definitionId),
                cancellationToken)
            .ConfigureAwait(false);
        return existing.Count > 0;
    }

    private static async Task EnsureStandaloneResponseAsync(
        IServiceProvider services,
        CynaraDbContext dbContext,
        Guid hospitalId,
        Guid publishedVersionId,
        string publishedVersion,
        CancellationToken cancellationToken)
    {
        List<Guid> boundResponseIds = await dbContext.ClinicalDocuments
            .AsNoTracking()
            .Select(item => item.FormResponseId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        bool hasStandalone = await dbContext.FormResponses
            .AsNoTracking()
            .AnyAsync(
                item => item.HospitalId == hospitalId
                    && item.FormVersionId == publishedVersionId
                    && !boundResponseIds.Contains(item.Id),
                cancellationToken)
            .ConfigureAwait(false);
        if (hasStandalone)
        {
            return;
        }

        IFormResponseLifecycleService responses = services
            .GetRequiredService<IFormResponseLifecycleService>();
        FormResponseDto draft = await responses.CreateAsync(
            FormCode,
            publishedVersion,
            new CreateFormResponseRequest(LoadJson("demo-answers-draft.json")),
            ActorId,
            cancellationToken).ConfigureAwait(false);
        FormResponseDto updated = await responses.UpdateAsync(
            draft.Id,
            new UpdateFormResponseRequest(
                LoadJson("demo-answers-completed.json"),
                draft.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
        _ = await responses.CompleteAsync(
            updated.Id,
            new CompleteFormResponseRequest(updated.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureFailureLogAsync(
        CynaraDbContext dbContext,
        IFailureLogWriter writer,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        bool hasLog = await dbContext.FailureLogs
            .AsNoTracking()
            .AnyAsync(item => item.HospitalId == hospitalId, cancellationToken)
            .ConfigureAwait(false);
        if (hasLog)
        {
            return;
        }

        // Failure logs are append-only operational data; seed one sample
        // entry so the table is not empty in fresh workspaces.
        await writer.RecordAsync(
            new NotFoundException(
                "Fixture de seed: fallo no encontrado simulado para la demo."),
            new FailureRequestContext(
                Method: "GET",
                Path: "/forms/demo-showcase/missing",
                Query: null,
                ActorId: ActorId,
                TraceId: "seed-fixture",
                HospitalId: hospitalId),
            statusCode: 404,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsurePublishedFormAsync(
        IFormService forms,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        FormSummaryDto summary = await forms
            .GetSummaryAsync(FormCode, cancellationToken)
            .ConfigureAwait(false);
        if (summary.PublishedVersions.Count > 0)
        {
            return;
        }

        IFormReviewService review = services
            .GetRequiredService<IFormReviewService>();
        FormVersionDto editable = await forms
            .GetEditableVersionAsync(FormCode, cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(editable.Status, "draft", StringComparison.Ordinal))
        {
            _ = await review.SubmitForReviewAsync(
                FormCode,
                new SubmitFormDraftForReviewRequest(editable.RowVersion),
                ActorId,
                cancellationToken).ConfigureAwait(false);
            editable = await forms
                .GetEditableVersionAsync(FormCode, cancellationToken)
                .ConfigureAwait(false);
        }

        _ = await review.PublishDraftAsync(
            FormCode,
            new PublishFormDraftRequest(editable.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureWorkflowsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        IWorkflowQueryService queries = services
            .GetRequiredService<IWorkflowQueryService>();
        IReadOnlyList<WorkflowSummaryDto> existing = await queries
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        WorkflowSummaryDto? seeded = existing.FirstOrDefault(item =>
            string.Equals(item.Code, WorkflowCode, StringComparison.Ordinal));

        IWorkflowLifecycleService workflows = services
            .GetRequiredService<IWorkflowLifecycleService>();
        if (seeded is null)
        {
            _ = await workflows.CreateAsync(
                new CreateWorkflowRequest(
                    WorkflowCode,
                    "Triaje de pacientes",
                    PatientTriageWorkflowSchema),
                ActorId,
                cancellationToken).ConfigureAwait(false);
            seeded = await queries
                .GetSummaryAsync(WorkflowCode, cancellationToken)
                .ConfigureAwait(false);
        }

        if (seeded.PublishedVersions.Count > 0)
        {
            return;
        }

        uint rowVersion = seeded.EditableRowVersion
            ?? throw new InvalidOperationException(
                $"Workflow '{WorkflowCode}' has no editable version to publish.");
        if (string.Equals(seeded.EditableStatus, "draft", StringComparison.Ordinal))
        {
            WorkflowVersionDto inReview = await workflows
                .SubmitForReviewAsync(
                    WorkflowCode,
                    new SubmitWorkflowDraftForReviewRequest(rowVersion),
                    ActorId,
                    cancellationToken)
                .ConfigureAwait(false);
            rowVersion = inReview.RowVersion;
        }

        _ = await workflows.PublishDraftAsync(
            WorkflowCode,
            new PublishWorkflowDraftRequest(rowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureCapabilitiesAsync(
        ICapabilityAssignmentService capabilities,
        CancellationToken cancellationToken)
    {
        CapabilityAssignmentListResponse response = await capabilities
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        var held = response.Items
            .Where(item => string.Equals(
                item.ActorId,
                ActorId,
                StringComparison.Ordinal))
            .Select(item => item.Capability)
            .ToHashSet(StringComparer.Ordinal);

        // The demo actor drives every client flow, so seed the full Stage 2
        // capability catalog rather than a subset; assignments are idempotent.
        foreach (string capability in CapabilityCodes.All)
        {
            if (held.Contains(capability))
            {
                continue;
            }

            _ = await capabilities
                .GrantAsync(
                    new GrantCapabilityRequest(ActorId, capability),
                    ActorId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<Hospital> ResolveHospitalAsync(
        CynaraDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Hospital? hospital = await dbContext.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return hospital
            ?? await HospitalBootstrap.EnsureBootstrapHospitalAsync(
                dbContext,
                new HospitalBootstrapOptions
                {
                    BootstrapCode = HospitalBootstrap.DefaultBootstrapCode,
                    BootstrapName = HospitalBootstrap.DefaultBootstrapName,
                    HeaderName = HospitalBootstrapOptions.DefaultHeaderName,
                    AllowAutoBootstrap = true,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureComponentAsync(
        IComponentQueryService componentQueries,
        IComponentLifecycleService componentLifecycle,
        CancellationToken cancellationToken)
    {
        bool exists;
        try
        {
            _ = await componentQueries
                .GetSummaryAsync(ComponentCode, cancellationToken)
                .ConfigureAwait(false);
            exists = true;
        }
        catch (NotFoundException)
        {
            exists = false;
        }

        if (exists)
        {
            return;
        }

        ComponentSummaryDto summary = await componentLifecycle.CreateAsync(
            new CreateComponentRequest(
                ComponentCode,
                "Datos demográficos del paciente",
                LoadJson("patient-demographics-clinical.json"),
                LoadJson("patient-demographics-ui.json")),
            ActorId,
            cancellationToken).ConfigureAwait(false);
        ComponentVersionDto draft = await componentQueries.GetDraftAsync(
            summary.Code,
            cancellationToken).ConfigureAwait(false);
        _ = await componentLifecycle.PublishDraftAsync(
            ComponentCode,
            new PublishComponentDraftRequest(draft.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertFormAsync(
        IFormService forms,
        CancellationToken cancellationToken)
    {
        string clinical = LoadJson("demo-showcase-clinical.json");
        string ui = LoadJson("demo-showcase-ui.json");
        string rules = LoadJson("demo-showcase-rules.json");

        IReadOnlyList<FormSummaryDto> existingForms = await forms
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        FormSummaryDto? existing = existingForms.FirstOrDefault(
            item => string.Equals(item.Code, FormCode, StringComparison.Ordinal));

        if (existing is null)
        {
            _ = await forms.CreateAsync(
                new CreateFormRequest(
                    FormCode,
                    "Showcase clínico (vista previa)",
                    clinical,
                    ui,
                    rules),
                ActorId,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(existing.EditableStatus, "review", StringComparison.Ordinal))
        {
            return;
        }

        FormVersionDto draft;
        if (existing.EditableVersionId is null)
        {
            draft = await forms
                .CreateDraftFromLatestAsync(FormCode, ActorId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            draft = await forms
                .GetEditableVersionAsync(FormCode, cancellationToken)
                .ConfigureAwait(false);
        }

        _ = await forms.UpdateDraftAsync(
            FormCode,
            new UpdateFormDraftRequest(
                clinical,
                ui,
                rules,
                draft.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    private static string LoadJson(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SeedData", fileName);
        return JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)));
    }
}
