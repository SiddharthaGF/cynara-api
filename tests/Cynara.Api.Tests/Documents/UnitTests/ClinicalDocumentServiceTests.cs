using Cynara.Api.Tests.Documents.UnitTests.Fakes;
using Cynara.Api.Tests.Support;

using Cynara.Application;
using Cynara.Application.Common;
using Cynara.Application.Modules.Documents;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Forms;

namespace Cynara.Api.Tests.Documents.UnitTests;

/// <summary>
/// Unit coverage for <see cref="ClinicalDocumentService"/>. The service is
/// the boundary that enforces the CYN-39 invariants: tenant scoping, active
/// catalog entries, open encounters, published-only form version pinning,
/// the single/multiple-instance catalog policy, and audit emission. The
/// integration tests cover the happy path against Postgres; these tests pin
/// each branch the integration suite does not exercise.
/// </summary>
public sealed class ClinicalDocumentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_RequiresResolvedTenant()
    {
        var harness = ServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.StartAsync(
                harness.NewRequest(), "actor", CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_RejectsUnknownDefinition()
    {
        var harness = ServiceHarness.Create();
        StartClinicalDocumentRequest request = new(
            Guid.NewGuid(), harness.Encounter.Id);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.StartAsync(
                request, "actor", CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_RejectsRetiredDefinition()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition retired = harness.BuildDefinition(
            status: DocumentDefinitionStatus.Retired,
            pinnedVersionId: harness.FormVersion.Id);
        harness.Catalog.Seed(retired);

        InvalidStateException ex = await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.StartAsync(
                new StartClinicalDocumentRequest(
                    retired.Id, harness.Encounter.Id),
                "actor",
                CancellationToken.None));

        Assert.Contains("retired", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_RejectsUnknownEncounter()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.ActiveDefinition();

        NotFoundException ex = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.StartAsync(
                new StartClinicalDocumentRequest(
                    definition.Id, Guid.NewGuid()),
                "actor",
                CancellationToken.None));

        Assert.Contains("Encounter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_RejectsNonOpenEncounter()
    {
        var harness = ServiceHarness.Create();
        Encounter completed = harness.BuildEncounter(
            status: EncounterStatus.Completed);
        harness.Encounters.Seed(completed);

        InvalidStateException ex = await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.StartAsync(
                new StartClinicalDocumentRequest(
                    harness.ActiveDefinition().Id, completed.Id),
                "actor",
                CancellationToken.None));

        Assert.Contains("open", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_RejectsUnknownPinnedFormVersion()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.BuildDefinition(
            status: DocumentDefinitionStatus.Active,
            pinnedVersionId: Guid.NewGuid());
        harness.Catalog.Seed(definition);

        NotFoundException ex = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.StartAsync(
                new StartClinicalDocumentRequest(
                    definition.Id, harness.Encounter.Id),
                "actor",
                CancellationToken.None));

        Assert.Contains("Form version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_RejectsUnpublishedPinnedFormVersion()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.BuildDefinition(
            status: DocumentDefinitionStatus.Active,
            pinnedVersionId: harness.DraftVersion.Id);
        harness.Catalog.Seed(definition);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Service.StartAsync(
                new StartClinicalDocumentRequest(
                    definition.Id, harness.Encounter.Id),
                "actor",
                CancellationToken.None));

        Assert.Contains("not published", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_RequiresActorWhenCatalogDemandsIt()
    {
        var harness = ServiceHarness.Create();

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.StartAsync(
                harness.NewRequest(), actorId: null, CancellationToken.None));

        Assert.Contains("actor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_RejectsDuplicateForSingleInstanceCatalog()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.ActiveDefinition();
        ClinicalDocument existing = harness.BuildDocument(
            definition,
            formVersionId: harness.FormVersion.Id);
        harness.Documents.Seed(existing);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Service.StartAsync(
                harness.NewRequest(), "actor", CancellationToken.None));

        Assert.Contains("single document", ex.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Documents.Added);
    }

    [Fact]
    public async Task StartAsync_AllowsMultipleInstancesForMultiInstanceCatalog()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.ActiveDefinition();
        harness.Catalog.Seed(
            harness.BuildDefinition(
                status: DocumentDefinitionStatus.Active,
                pinnedVersionId: harness.FormVersion.Id,
                code: "multi",
                allowsMultipleInstancesPerEncounter: true));
        harness.Documents.Seed(
            harness.BuildDocument(
                definition,
                formVersionId: harness.FormVersion.Id));
        DocumentDefinition multi = harness.Catalog.Entries
            .Single(item => string.Equals(item.Code, "multi", StringComparison.Ordinal));

        ClinicalDocumentDto created = await harness.Service.StartAsync(
            new StartClinicalDocumentRequest(
                multi.Id, harness.Encounter.Id),
            "actor",
            CancellationToken.None);

        Assert.Equal(multi.Id, created.DocumentDefinitionId);
        Assert.Single(harness.Documents.Added);
    }

    [Fact]
    public async Task StartAsync_CreatesDocumentResponseAndAudit()
    {
        var harness = ServiceHarness.Create();

        ClinicalDocumentDto created = await harness.Service.StartAsync(
            harness.NewRequest(), "actor", CancellationToken.None);

        ClinicalDocument document = Assert.Single(harness.Documents.Added);
        Assert.Equal(harness.HospitalId, document.HospitalId);
        Assert.Equal(harness.ActiveDefinition().Id, document.DocumentDefinitionId);
        Assert.Equal(harness.Encounter.PatientId, document.PatientId);
        Assert.Equal(harness.Encounter.Id, document.EncounterId);
        Assert.Equal(harness.FormVersion.Id, document.FormVersionId);
        Assert.Equal("actor", document.AuthorId);
        Assert.Equal(ClinicalDocumentStatus.InProgress, document.Status);
        Assert.Null(document.CompletedAt);
        Assert.Equal(Now, document.CreatedAt);
        Assert.Equal(Now, document.UpdatedAt);

        FormResponse response = Assert.Single(harness.Responses.Responses);
        Assert.Equal(harness.FormVersion.Id, response.FormVersionId);
        Assert.Equal(FormResponseStatus.Draft, response.Status);
        Assert.Equal("{}", response.AnswersJson);
        Assert.Equal(1u, response.RevisionNumber);
        Assert.Equal(created.FormResponseId, response.Id);

        FormResponseRevision revision = Assert.Single(
            harness.Responses.AddedRevisions);
        Assert.Equal(1u, revision.RevisionNumber);

        RecordingAuditWriter.AuditEntry audit = Assert.Single(
            harness.AuditWriter.Entries);
        Assert.Equal(AuditEntityTypes.ClinicalDocument, audit.ResourceType);
        Assert.Equal(document.Id, audit.ResourceId);
        Assert.Equal("document.started", audit.Action);
        Assert.Equal("actor", audit.ActorId);
        Assert.Equal(Now, audit.OccurredAt);

        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);
        Assert.Equal("inProgress", created.Status);
        Assert.Equal(0u, created.RowVersion);
    }

    [Fact]
    public async Task GetAsync_RequiresResolvedTenant()
    {
        var harness = ServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenUnknown()
    {
        var harness = ServiceHarness.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyOwnTenantDocuments()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.ActiveDefinition();
        ClinicalDocument mine = harness.BuildDocument(
            definition, formVersionId: harness.FormVersion.Id);
        ClinicalDocument theirs = harness.BuildDocument(
            definition,
            formVersionId: harness.FormVersion.Id,
            hospitalId: Guid.NewGuid());
        harness.Documents.Seed(mine, theirs);

        ClinicalDocumentDto actual = await harness.Service.GetAsync(
            mine.Id, CancellationToken.None);

        Assert.Equal(mine.Id, actual.Id);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.GetAsync(theirs.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_RequiresResolvedTenant()
    {
        var harness = ServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.ListAsync(
                new ClinicalDocumentListRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_FiltersByEncounterAndStatus()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.ActiveDefinition();
        var otherEncounterId = Guid.NewGuid();
        ClinicalDocument inProgress = harness.BuildDocument(
            definition, formVersionId: harness.FormVersion.Id);
        ClinicalDocument otherEncounter = harness.BuildDocument(
            definition,
            formVersionId: harness.FormVersion.Id,
            encounterId: otherEncounterId);
        ClinicalDocument completed = harness.BuildDocument(
            definition,
            formVersionId: harness.FormVersion.Id,
            encounterId: otherEncounterId,
            status: ClinicalDocumentStatus.Completed);
        harness.Documents.Seed(inProgress, otherEncounter, completed);

        IReadOnlyList<ClinicalDocumentDto> matches =
            await harness.Service.ListAsync(
                new ClinicalDocumentListRequest(
                    EncounterId: harness.Encounter.Id),
                CancellationToken.None);

        Assert.Single(matches);
        Assert.Equal(inProgress.Id, matches[0].Id);
        Assert.Equal("inProgress", matches[0].Status);

        IReadOnlyList<ClinicalDocumentDto> completedOnly =
            await harness.Service.ListAsync(
                new ClinicalDocumentListRequest(Status: "completed"),
                CancellationToken.None);

        Assert.Single(completedOnly);
        Assert.Equal(completed.Id, completedOnly[0].Id);
    }

    [Fact]
    public async Task ListAsync_RejectsInvalidStatus()
    {
        var harness = ServiceHarness.Create();

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.ListAsync(
                new ClinicalDocumentListRequest(Status: "bogus"),
                CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_RequiresResolvedTenant()
    {
        var harness = ServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.CompleteAsync(
                Guid.NewGuid(),
                new TransitionClinicalDocumentRequest(0),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_ThrowsWhenUnknown()
    {
        var harness = ServiceHarness.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.CompleteAsync(
                Guid.NewGuid(),
                new TransitionClinicalDocumentRequest(0),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_RequiresActorWhenCatalogDemandsIt()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.ActiveDefinition();
        (ClinicalDocument document, _) = harness.SeedBoundDocument(
            definition, ClinicalDocumentStatus.InProgress);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.CompleteAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(document.RowVersion),
                actorId: null,
                CancellationToken.None));

        Assert.Contains("actor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ClinicalDocumentStatus.InProgress, document.Status);
    }

    [Fact]
    public async Task CompleteAsync_CompletesDocumentAndResponseAtomically()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, FormResponse response) =
            harness.SeedBoundDocument(
                harness.ActiveDefinition(), ClinicalDocumentStatus.InProgress);

        ClinicalDocumentDto completed = await harness.Service.CompleteAsync(
            document.Id,
            new TransitionClinicalDocumentRequest(document.RowVersion),
            "actor",
            CancellationToken.None);

        Assert.Equal("completed", completed.Status);
        Assert.Equal(Now, completed.CompletedAt);
        Assert.Equal(1u, completed.RowVersion);
        Assert.Equal(ClinicalDocumentStatus.Completed, document.Status);
        Assert.Equal(Now, document.CompletedAt);
        Assert.Equal(1u, document.RowVersion);

        Assert.Equal(FormResponseStatus.Completed, response.Status);
        Assert.Equal(2u, response.RevisionNumber);
        Assert.Equal(1u, response.RowVersion);
        Assert.Equal(Now, response.CompletedAt);

        FormResponseRevision revision = harness.Responses.AddedRevisions
            .Single(item => item.FormResponseId == response.Id);
        Assert.Equal(2u, revision.RevisionNumber);
        Assert.Equal("actor", revision.ActorId);

        RecordingAuditWriter.AuditEntry audit =
            harness.AuditWriter.Entries.Single(
                item => string.Equals(
                    item.Action, "document.completed", StringComparison.Ordinal));
        Assert.Equal(document.Id, audit.ResourceId);
        Assert.Equal("actor", audit.ActorId);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CompleteAsync_RejectsCompletedDocumentWithoutMutating()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, FormResponse response) =
            harness.SeedBoundDocument(
                harness.ActiveDefinition(),
                ClinicalDocumentStatus.Completed,
                FormResponseStatus.Completed);

        uint documentRowVersion = document.RowVersion;
        uint revisionBefore = response.RevisionNumber;
        uint responseRowVersion = response.RowVersion;
        string answersBefore = response.AnswersJson;

        await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.CompleteAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(document.RowVersion),
                "actor",
                CancellationToken.None));

        Assert.Equal(ClinicalDocumentStatus.Completed, document.Status);
        Assert.Equal(documentRowVersion, document.RowVersion);
        Assert.Equal(FormResponseStatus.Completed, response.Status);
        Assert.Equal(revisionBefore, response.RevisionNumber);
        Assert.Equal(responseRowVersion, response.RowVersion);
        Assert.Equal(answersBefore, response.AnswersJson);
    }

    [Fact]
    public async Task CompleteAsync_RejectsStaleConcurrency()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, FormResponse response) =
            harness.SeedBoundDocument(
                harness.ActiveDefinition(), ClinicalDocumentStatus.InProgress);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => harness.Service.CompleteAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(
                    document.RowVersion + 1),
                "actor",
                CancellationToken.None));

        Assert.Equal(ClinicalDocumentStatus.InProgress, document.Status);
        Assert.Equal(FormResponseStatus.Draft, response.Status);
    }

    [Fact]
    public async Task CompleteAsync_RejectsNonDraftResponse()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, FormResponse response) =
            harness.SeedBoundDocument(
                harness.ActiveDefinition(),
                ClinicalDocumentStatus.InProgress,
                FormResponseStatus.Completed);

        InvalidStateException ex = await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.CompleteAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(document.RowVersion),
                "actor",
                CancellationToken.None));

        Assert.Contains("draft", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ClinicalDocumentStatus.InProgress, document.Status);
        Assert.Equal(FormResponseStatus.Completed, response.Status);
    }

    [Fact]
    public async Task CancelAsync_CancelsDocumentAndLeavesResponseIntact()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, FormResponse response) =
            harness.SeedBoundDocument(
                harness.ActiveDefinition(), ClinicalDocumentStatus.InProgress);

        ClinicalDocumentDto canceled = await harness.Service.CancelAsync(
            document.Id,
            new TransitionClinicalDocumentRequest(document.RowVersion),
            "actor",
            CancellationToken.None);

        Assert.Equal("canceled", canceled.Status);
        Assert.Equal(Now, canceled.CanceledAt);
        Assert.Equal(ClinicalDocumentStatus.Canceled, document.Status);
        Assert.Equal(FormResponseStatus.Draft, response.Status);

        RecordingAuditWriter.AuditEntry audit = Assert.Single(
            harness.AuditWriter.Entries);
        Assert.Equal("document.canceled", audit.Action);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CancelAsync_RejectsCompletedDocument()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, _) = harness.SeedBoundDocument(
            harness.ActiveDefinition(), ClinicalDocumentStatus.Completed);

        InvalidStateException ex = await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.CancelAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(document.RowVersion),
                "actor",
                CancellationToken.None));

        Assert.Contains("cancel", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ClinicalDocumentStatus.Completed, document.Status);
    }

    [Fact]
    public async Task CancelAsync_RejectsStaleConcurrency()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, _) = harness.SeedBoundDocument(
            harness.ActiveDefinition(), ClinicalDocumentStatus.InProgress);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => harness.Service.CancelAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(
                    document.RowVersion + 1),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task EnterInErrorAsync_MarksCompletedDocument()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, _) = harness.SeedBoundDocument(
            harness.ActiveDefinition(),
            ClinicalDocumentStatus.Completed,
            FormResponseStatus.Completed);

        ClinicalDocumentDto marked = await harness.Service.EnterInErrorAsync(
            document.Id,
            new TransitionClinicalDocumentRequest(
                document.RowVersion, "Wrong result"),
            "reviewer",
            CancellationToken.None);

        Assert.Equal("enteredInError", marked.Status);
        Assert.Equal("Wrong result", marked.EnteredInErrorReason);
        Assert.Equal("reviewer", marked.EnteredInErrorById);
        Assert.Equal(Now, marked.EnteredInErrorAt);
        Assert.Equal(ClinicalDocumentStatus.EnteredInError, document.Status);

        RecordingAuditWriter.AuditEntry audit = Assert.Single(
            harness.AuditWriter.Entries);
        Assert.Equal("document.enteredInError", audit.Action);
        Assert.Equal("reviewer", audit.ActorId);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task EnterInErrorAsync_RequiresReason()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, _) = harness.SeedBoundDocument(
            harness.ActiveDefinition(), ClinicalDocumentStatus.InProgress);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.EnterInErrorAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(
                    document.RowVersion, Reason: null),
                "actor",
                CancellationToken.None));

        Assert.Contains("reason", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ClinicalDocumentStatus.InProgress, document.Status);
    }

    [Fact]
    public async Task EnterInErrorAsync_RequiresActor()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, _) = harness.SeedBoundDocument(
            harness.ActiveDefinition(), ClinicalDocumentStatus.InProgress);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.EnterInErrorAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(
                    document.RowVersion, "Wrong result"),
                actorId: null,
                CancellationToken.None));

        Assert.Contains("actor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ClinicalDocumentStatus.InProgress, document.Status);
    }

    [Fact]
    public async Task EnterInErrorAsync_RejectsStaleConcurrency()
    {
        var harness = ServiceHarness.Create();
        (ClinicalDocument document, _) = harness.SeedBoundDocument(
            harness.ActiveDefinition(), ClinicalDocumentStatus.InProgress);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => harness.Service.EnterInErrorAsync(
                document.Id,
                new TransitionClinicalDocumentRequest(
                    document.RowVersion + 1, "Wrong result"),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_FiltersByCanceledAndEnteredInError()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition definition = harness.ActiveDefinition();
        ClinicalDocument canceled = harness.BuildDocument(
            definition,
            formVersionId: harness.FormVersion.Id,
            status: ClinicalDocumentStatus.Canceled);
        ClinicalDocument enteredInError = harness.BuildDocument(
            definition,
            formVersionId: harness.FormVersion.Id,
            status: ClinicalDocumentStatus.EnteredInError);
        harness.Documents.Seed(canceled, enteredInError);

        IReadOnlyList<ClinicalDocumentDto> canceledOnly =
            await harness.Service.ListAsync(
                new ClinicalDocumentListRequest(Status: "canceled"),
                CancellationToken.None);

        Assert.Single(canceledOnly);
        Assert.Equal(canceled.Id, canceledOnly[0].Id);

        IReadOnlyList<ClinicalDocumentDto> enteredInErrorOnly =
            await harness.Service.ListAsync(
                new ClinicalDocumentListRequest(Status: "enteredInError"),
                CancellationToken.None);

        Assert.Single(enteredInErrorOnly);
        Assert.Equal(enteredInError.Id, enteredInErrorOnly[0].Id);
    }

    private sealed class ServiceHarness
    {
        private ServiceHarness(
            FakeClinicalDocumentRepository documents,
            FakeClinicalDocumentReferenceResolver references,
            FakeDocumentCatalogRepository catalog,
            FakeEncounterRepository encounters,
            FakeFormResponseRepository responses,
            RecordingUnitOfWork unitOfWork,
            RecordingAuditWriter auditWriter,
            FakeHospitalContext hospitalContext,
            FixedTimeProvider timeProvider,
            DocumentDefinition definition,
            FormVersion formVersion,
            FormVersion draftVersion,
            Encounter encounter)
        {
            Documents = documents;
            References = references;
            Catalog = catalog;
            Encounters = encounters;
            Responses = responses;
            UnitOfWork = unitOfWork;
            AuditWriter = auditWriter;
            HospitalContext = hospitalContext;
            TimeProvider = timeProvider;
            Definition = definition;
            FormVersion = formVersion;
            DraftVersion = draftVersion;
            Encounter = encounter;
            Service = new ClinicalDocumentService(
                documents,
                references,
                new FakeClinicalDocumentResponseStage(
                    responses, new FakeFormResponseValidator()),
                unitOfWork,
                auditWriter,
                new FakeWorkflowContext(hospitalContext, timeProvider),
                new FakeCapabilityGuard());
        }

        public FakeClinicalDocumentRepository Documents { get; }

        public FakeClinicalDocumentReferenceResolver References { get; }

        public FakeDocumentCatalogRepository Catalog { get; }

        public FakeEncounterRepository Encounters { get; }

        public FakeFormResponseRepository Responses { get; }

        public RecordingUnitOfWork UnitOfWork { get; }

        public RecordingAuditWriter AuditWriter { get; }

        public FakeHospitalContext HospitalContext { get; }

        public FixedTimeProvider TimeProvider { get; }

        public DocumentDefinition Definition { get; }

        public FormVersion FormVersion { get; }

        public FormVersion DraftVersion { get; }

        public Encounter Encounter { get; }

        public ClinicalDocumentService Service { get; }

        public Guid HospitalId => HospitalContext.HospitalId;

        public StartClinicalDocumentRequest NewRequest()
        {
            return new StartClinicalDocumentRequest(
                Definition.Id, Encounter.Id);
        }

        public DocumentDefinition ActiveDefinition()
        {
            return Definition;
        }

        public DocumentDefinition BuildDefinition(
            DocumentDefinitionStatus status,
            Guid pinnedVersionId,
            string code = "lab-result",
            bool allowsMultipleInstancesPerEncounter = false)
        {
            return new DocumentDefinition
            {
                Id = Guid.NewGuid(),
                HospitalId = HospitalId,
                Code = code,
                Name = $"Name for {code}",
                Status = status,
                FormDefinitionId = Guid.NewGuid(),
                FormVersionId = pinnedVersionId,
                FacilityId = Guid.NewGuid(),
                ClinicalAreaId = Guid.NewGuid(),
                DisciplineId = Guid.NewGuid(),
                AllowsMultipleInstancesPerEncounter =
                    allowsMultipleInstancesPerEncounter,
                RequiresActorForCreation = true,
                RequiresActorForCompletion = true,
                CreatedAt = Now,
                UpdatedAt = Now,
                RowVersion = 0u,
            };
        }

        public Encounter BuildEncounter(EncounterStatus status)
        {
            return new Encounter
            {
                Id = Guid.NewGuid(),
                HospitalId = HospitalId,
                PatientId = Guid.NewGuid(),
                FacilityId = Guid.NewGuid(),
                ClinicalAreaId = Guid.NewGuid(),
                Type = EncounterType.Ambulatory,
                ResponsibleProfessionalId = "dr-who",
                Status = status,
                StartedAt = Now,
                CreatedAt = Now,
                UpdatedAt = Now,
                RowVersion = 0u,
            };
        }

        public ClinicalDocument BuildDocument(
            DocumentDefinition definition,
            Guid formVersionId,
            Guid? hospitalId = null,
            Guid? encounterId = null,
            ClinicalDocumentStatus status = ClinicalDocumentStatus.InProgress)
        {
            return new ClinicalDocument
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId ?? HospitalId,
                DocumentDefinitionId = definition.Id,
                PatientId = Encounter.PatientId,
                EncounterId = encounterId ?? Encounter.Id,
                FormVersionId = formVersionId,
                FormResponseId = Guid.NewGuid(),
                AuthorId = "actor",
                Status = status,
                CreatedAt = Now,
                UpdatedAt = Now,
                RowVersion = 0u,
            };
        }

        public (ClinicalDocument Document, FormResponse Response) SeedBoundDocument(
            DocumentDefinition definition,
            ClinicalDocumentStatus status,
            FormResponseStatus responseStatus = FormResponseStatus.Draft)
        {
            var response = new FormResponse
            {
                Id = Guid.NewGuid(),
                HospitalId = HospitalId,
                FormVersionId = FormVersion.Id,
                Status = responseStatus,
                AnswersJson = "{}",
                RevisionNumber = 1u,
                RowVersion = 0u,
                CreatedAt = Now,
                UpdatedAt = Now,
                CompletedAt = responseStatus == FormResponseStatus.Completed
                    ? Now
                    : null,
            };
            Responses.Seed(response);

            ClinicalDocument document = BuildDocument(
                definition, FormVersion.Id);
            document.FormResponseId = response.Id;
            document.Status = status;
            Documents.Seed(document);
            return (document, response);
        }

        public static ServiceHarness Create()
        {
            var hospitalId = Guid.NewGuid();
            var formDefinitionId = Guid.NewGuid();
            var formVersionId = Guid.NewGuid();
            var draftVersionId = Guid.NewGuid();

            FakeFormRepository formRepository = new();
            formRepository.Seed(new FormDefinition
            {
                Id = formDefinitionId,
                HospitalId = hospitalId,
                Code = "published-form",
                Name = "Published form",
                CreatedAt = Now,
                UpdatedAt = Now,
                Versions = new HashSet<FormVersion>
                {
                    new()
                    {
                        Id = formVersionId,
                        HospitalId = hospitalId,
                        FormDefinitionId = formDefinitionId,
                        ClinicalSchemaJson = "{}",
                        Status = FormVersionStatus.Published,
                        Version = "1.0.0",
                        CreatedAt = Now,
                        PublishedAt = Now,
                    },
                    new()
                    {
                        Id = draftVersionId,
                        HospitalId = hospitalId,
                        FormDefinitionId = formDefinitionId,
                        ClinicalSchemaJson = "{}",
                        Status = FormVersionStatus.Draft,
                        Version = "1.1.0",
                        CreatedAt = Now,
                    },
                },
            });

            FakeEncounterRepository encounterRepository = new();
            var encounter = new Encounter
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                PatientId = Guid.NewGuid(),
                FacilityId = Guid.NewGuid(),
                ClinicalAreaId = Guid.NewGuid(),
                Type = EncounterType.Ambulatory,
                ResponsibleProfessionalId = "dr-who",
                Status = EncounterStatus.Open,
                StartedAt = Now,
                CreatedAt = Now,
                UpdatedAt = Now,
                RowVersion = 0u,
            };
            encounterRepository.Seed(encounter);

            DocumentDefinition definition = new()
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                Code = "lab-result",
                Name = "Lab result",
                Status = DocumentDefinitionStatus.Active,
                FormDefinitionId = formDefinitionId,
                FormVersionId = formVersionId,
                FacilityId = Guid.NewGuid(),
                ClinicalAreaId = Guid.NewGuid(),
                DisciplineId = Guid.NewGuid(),
                AllowsMultipleInstancesPerEncounter = false,
                RequiresActorForCreation = true,
                RequiresActorForCompletion = true,
                CreatedAt = Now,
                UpdatedAt = Now,
                RowVersion = 0u,
            };

            FakeDocumentCatalogRepository catalogRepository = new();
            catalogRepository.Seed(definition);

            var references = new FakeClinicalDocumentReferenceResolver(
                catalogRepository, encounterRepository, formRepository);

            return new ServiceHarness(
                new FakeClinicalDocumentRepository(),
                references,
                catalogRepository,
                encounterRepository,
                new FakeFormResponseRepository(),
                new RecordingUnitOfWork(),
                new RecordingAuditWriter(),
                new FakeHospitalContext(hospitalId),
                new FixedTimeProvider(Now),
                definition,
                formRepository.Definitions
                    .Single(item => string.Equals(item.Code, "published-form", StringComparison.Ordinal))
                    .Versions.Single(item => item.Id == formVersionId),
                formRepository.Definitions
                    .Single(item => string.Equals(item.Code, "published-form", StringComparison.Ordinal))
                    .Versions.Single(item => item.Id == draftVersionId),
                encounter);
        }
    }
}
