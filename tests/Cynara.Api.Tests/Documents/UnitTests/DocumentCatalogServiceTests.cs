using System.Globalization;
using System.Reflection;

using Cynara.Api.Tests.Documents.UnitTests.Fakes;
using Cynara.Application;
using Cynara.Application.Common;
using Cynara.Application.Modules.Documents;
using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Documents;
using Cynara.Domain.Forms;

using DataAnnotationsValidationException = System.ComponentModel.DataAnnotations.ValidationException;

namespace Cynara.Api.Tests.Documents.UnitTests;

/// <summary>
/// Unit coverage for <see cref="DocumentCatalogService"/>. The service is
/// the boundary that enforces the CYN-36 invariants: tenant scoping,
/// published-only form versions, taxonomy hierarchy, duplicate codes,
/// concurrency, and audit emission. The integration tests cover the
/// happy path against Postgres; these tests pin each branch that the
/// integration suite does not exercise (cross-tenant, retired, draft,
/// unknown form version, etc.).
/// </summary>
public sealed class DocumentCatalogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_RequiresResolvedTenant()
    {
        var harness = ServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.ListAsync(includeRetired: false, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_HidesRetiredEntriesByDefault()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition active = BuildDefinition(
            harness.HospitalId,
            code: "active-doc",
            status: DocumentDefinitionStatus.Active);
        DocumentDefinition retired = BuildDefinition(
            harness.HospitalId,
            code: "retired-doc",
            status: DocumentDefinitionStatus.Retired);
        harness.Repository.Seed(active, retired);

        var entries = await harness.Service.ListAsync(
            includeRetired: false,
            CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("active-doc", entries[0].Code);
    }

    [Fact]
    public async Task ListAsync_IncludesRetiredWhenRequested()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition active = BuildDefinition(
            harness.HospitalId,
            code: "active-doc",
            status: DocumentDefinitionStatus.Active);
        DocumentDefinition retired = BuildDefinition(
            harness.HospitalId,
            code: "retired-doc",
            status: DocumentDefinitionStatus.Retired);
        harness.Repository.Seed(active, retired);

        var entries = await harness.Service.ListAsync(
            includeRetired: true,
            CancellationToken.None);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task ListAsync_HidesOtherTenantEntries()
    {
        var harness = ServiceHarness.Create();
        var otherHospitalId = Guid.NewGuid();
        harness.Repository.Seed(
            BuildDefinition(harness.HospitalId, code: "mine", status: DocumentDefinitionStatus.Active),
            BuildDefinition(otherHospitalId, code: "theirs", status: DocumentDefinitionStatus.Active));

        var entries = await harness.Service.ListAsync(
            includeRetired: false,
            CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("mine", entries[0].Code);
    }

    [Fact]
    public async Task CreateAsync_RejectsBlankCode()
    {
        var harness = ServiceHarness.Create();
        CreateDocumentDefinitionRequest request = NewRequest(harness, code: "   ");

        await Assert.ThrowsAsync<DataAnnotationsValidationException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RejectsBlankName()
    {
        var harness = ServiceHarness.Create();
        CreateDocumentDefinitionRequest request = NewRequest(harness, name: string.Empty);

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenFormVersionUnknown()
    {
        var harness = ServiceHarness.Create();
        CreateDocumentDefinitionRequest request = new(
            "code",
            "Name",
            Guid.NewGuid(),
            harness.FacilityId,
            harness.ClinicalAreaId,
            harness.DisciplineId);

        NotFoundException exception = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));

        Assert.Contains("Form version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenFormVersionNotPublished()
    {
        var harness = ServiceHarness.Create();
        var formVersionId = Guid.NewGuid();
        var formDefinitionId = Guid.NewGuid();
        harness.FormRepository.Seed(new FormDefinition
        {
            Id = formDefinitionId,
            HospitalId = harness.HospitalId,
            Code = "draft-form",
            Name = "Draft form",
            CreatedAt = Now,
            UpdatedAt = Now,
            Versions = new HashSet<FormVersion>
            {
                new()
                {
                    Id = formVersionId,
                    HospitalId = harness.HospitalId,
                    FormDefinitionId = formDefinitionId,
                    ClinicalSchemaJson = "{}",
                    Status = FormVersionStatus.Draft,
                    CreatedAt = Now,
                },
            },
        });
        CreateDocumentDefinitionRequest request = new(
            "code",
            "Name",
            formVersionId,
            harness.FacilityId,
            harness.ClinicalAreaId,
            harness.DisciplineId);

        ConflictException exception = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));

        Assert.Contains("not published", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenFacilityMissing()
    {
        var harness = ServiceHarness.Create();
        var unpublishedFormVersionId = Guid.NewGuid();
        var formDefinitionId = Guid.NewGuid();
        harness.FormRepository.Seed(BuildPublishedForm(
            harness.HospitalId,
            formDefinitionId,
            unpublishedFormVersionId));
        CreateDocumentDefinitionRequest request = new(
            "code",
            "Name",
            Guid.NewGuid(),
            Guid.NewGuid(),
            harness.ClinicalAreaId,
            harness.DisciplineId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenClinicalAreaMissing()
    {
        var harness = ServiceHarness.Create();
        var formVersionId = Guid.NewGuid();
        var formDefinitionId = Guid.NewGuid();
        harness.FormRepository.Seed(BuildPublishedForm(
            harness.HospitalId,
            formDefinitionId,
            formVersionId));
        CreateDocumentDefinitionRequest request = new(
            "code",
            "Name",
            formVersionId,
            harness.FacilityId,
            Guid.NewGuid(),
            harness.DisciplineId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDisciplineMissing()
    {
        var harness = ServiceHarness.Create();
        var formVersionId = Guid.NewGuid();
        var formDefinitionId = Guid.NewGuid();
        harness.FormRepository.Seed(BuildPublishedForm(
            harness.HospitalId,
            formDefinitionId,
            formVersionId));
        CreateDocumentDefinitionRequest request = new(
            "code",
            "Name",
            formVersionId,
            harness.FacilityId,
            harness.ClinicalAreaId,
            Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenClinicalAreaNotUnderFacility()
    {
        var harness = ServiceHarness.Create();
        var otherFacilityId = Guid.NewGuid();
        harness.TaxonomyRepository.SeedFacility(new Facility
        {
            Id = otherFacilityId,
            HospitalId = harness.HospitalId,
            Code = "other-facility",
            Name = "Other facility",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        var formVersionId = Guid.NewGuid();
        var formDefinitionId = Guid.NewGuid();
        harness.FormRepository.Seed(BuildPublishedForm(
            harness.HospitalId,
            formDefinitionId,
            formVersionId));
        CreateDocumentDefinitionRequest request = new(
            "code",
            "Name",
            formVersionId,
            otherFacilityId,
            harness.ClinicalAreaId,
            harness.DisciplineId);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));

        Assert.Contains("does not belong to", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDisciplineNotUnderClinicalArea()
    {
        var harness = ServiceHarness.Create();
        var otherAreaId = Guid.NewGuid();
        harness.TaxonomyRepository.SeedClinicalArea(new ClinicalArea
        {
            Id = otherAreaId,
            HospitalId = harness.HospitalId,
            Code = "other-area",
            Name = "Other area",
            FacilityId = harness.FacilityId,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        harness.TaxonomyRepository.SeedDiscipline(new Discipline
        {
            Id = otherAreaId,
            HospitalId = harness.HospitalId,
            Code = "orphan-discipline",
            Name = "Orphan discipline",
            ClinicalAreaId = otherAreaId,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        var formVersionId = Guid.NewGuid();
        var formDefinitionId = Guid.NewGuid();
        harness.FormRepository.Seed(BuildPublishedForm(
            harness.HospitalId,
            formDefinitionId,
            formVersionId));
        CreateDocumentDefinitionRequest request = new(
            "code",
            "Name",
            formVersionId,
            harness.FacilityId,
            harness.ClinicalAreaId,
            otherAreaId);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));

        Assert.Contains("does not belong to", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenCodeAlreadyExists()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "duplicate",
            status: DocumentDefinitionStatus.Active);
        harness.Repository.Seed(existing);
        CreateDocumentDefinitionRequest request = NewRequest(
            harness,
            code: "duplicate");

        ConflictException exception = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Service.CreateAsync(request, "actor", CancellationToken.None));

        Assert.Contains("duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_PersistsAndEmitsAudit()
    {
        var harness = ServiceHarness.Create();
        CreateDocumentDefinitionRequest request = NewRequest(harness);

        DocumentDefinitionDto dto = await harness.Service.CreateAsync(
            request,
            "actor",
            CancellationToken.None);

        Assert.Single(harness.Repository.Added);
        Assert.Equal(RequestConstants.StampedCode, dto.Code);
        Assert.Equal("active", dto.Status);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);

        AuditEntryDto audit = Assert.Single(harness.AuditWriter.Entries.Select(AuditEntryDto.From));
        Assert.Equal(AuditEntityTypes.DocumentDefinition, audit.ResourceType);
        Assert.Equal("document-definition.created", audit.Action);
        Assert.Equal("actor", audit.ActorId);
        Assert.Equal(Now, audit.OccurredAt);
        Assert.Equal(RequestConstants.StampedCode, audit.MetadataCode);
    }

    [Fact]
    public async Task UpdateAsync_RequiresResolvedTenant()
    {
        var harness = ServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.UpdateAsync(
                Guid.NewGuid(),
                new UpdateDocumentDefinitionRequest(
                    "name",
                    AllowsMultipleInstancesPerEncounter: true,
                    RequiresActorForCreation: true,
                    RequiresActorForCompletion: true,
                    RowVersion: 0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenDefinitionMissing()
    {
        var harness = ServiceHarness.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.UpdateAsync(
                Guid.NewGuid(),
                new UpdateDocumentDefinitionRequest(
                    "name",
                    AllowsMultipleInstancesPerEncounter: true,
                    RequiresActorForCreation: true,
                    RequiresActorForCompletion: true,
                    RowVersion: 0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenRowVersionMismatches()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "stale",
            status: DocumentDefinitionStatus.Active);
        existing.RowVersion = 5u;
        harness.Repository.Seed(existing);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => harness.Service.UpdateAsync(
                existing.Id,
                new UpdateDocumentDefinitionRequest(
                    "name",
                    AllowsMultipleInstancesPerEncounter: true,
                    RequiresActorForCreation: true,
                    RequiresActorForCompletion: true,
                    RowVersion: 0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenAlreadyRetired()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "retired",
            status: DocumentDefinitionStatus.Retired);
        harness.Repository.Seed(existing);

        await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.UpdateAsync(
                existing.Id,
                new UpdateDocumentDefinitionRequest(
                    "name",
                    AllowsMultipleInstancesPerEncounter: true,
                    RequiresActorForCreation: true,
                    RequiresActorForCompletion: true,
                    RowVersion: 0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsBlankName()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "blank",
            status: DocumentDefinitionStatus.Active);
        harness.Repository.Seed(existing);

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.UpdateAsync(
                existing.Id,
                new UpdateDocumentDefinitionRequest(
                    "   ",
                    AllowsMultipleInstancesPerEncounter: true,
                    RequiresActorForCreation: true,
                    RequiresActorForCompletion: true,
                    RowVersion: 0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_AdvancesRowVersionAndEmitsAudit()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "ok",
            status: DocumentDefinitionStatus.Active);
        existing.RowVersion = 3u;
        harness.Repository.Seed(existing);

        DocumentDefinitionDto dto = await harness.Service.UpdateAsync(
            existing.Id,
            new UpdateDocumentDefinitionRequest(
                "New name",
                AllowsMultipleInstancesPerEncounter: false,
                RequiresActorForCreation: true,
                RequiresActorForCompletion: false,
                RowVersion: 3u),
            "actor",
            CancellationToken.None);

        Assert.Equal("New name", dto.Name);
        Assert.Equal(4u, dto.RowVersion);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);

        AuditEntryDto audit = Assert.Single(harness.AuditWriter.Entries.Select(AuditEntryDto.From));
        Assert.Equal("document-definition.updated", audit.Action);
        Assert.Equal(3u, audit.MetadataRowVersion);
    }

    [Fact]
    public async Task RetireAsync_RequiresResolvedTenant()
    {
        var harness = ServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.RetireAsync(
                Guid.NewGuid(),
                new RetireDocumentDefinitionRequest(0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_ThrowsWhenDefinitionMissing()
    {
        var harness = ServiceHarness.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.RetireAsync(
                Guid.NewGuid(),
                new RetireDocumentDefinitionRequest(0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_ThrowsWhenAlreadyRetired()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "retired",
            status: DocumentDefinitionStatus.Retired);
        harness.Repository.Seed(existing);

        await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.RetireAsync(
                existing.Id,
                new RetireDocumentDefinitionRequest(0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_ThrowsWhenRowVersionMismatches()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "stale",
            status: DocumentDefinitionStatus.Active);
        existing.RowVersion = 5u;
        harness.Repository.Seed(existing);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => harness.Service.RetireAsync(
                existing.Id,
                new RetireDocumentDefinitionRequest(0u),
                "actor",
                CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_SetsStatusAndRetiredAt()
    {
        var harness = ServiceHarness.Create();
        DocumentDefinition existing = BuildDefinition(
            harness.HospitalId,
            code: "going-away",
            status: DocumentDefinitionStatus.Active);
        harness.Repository.Seed(existing);

        DocumentDefinitionDto dto = await harness.Service.RetireAsync(
            existing.Id,
            new RetireDocumentDefinitionRequest(0u),
            "actor",
            CancellationToken.None);

        Assert.Equal("retired", dto.Status);
        Assert.Equal(Now, dto.RetiredAt);
        Assert.Equal(1u, dto.RowVersion);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);

        AuditEntryDto audit = Assert.Single(harness.AuditWriter.Entries.Select(AuditEntryDto.From));
        Assert.Equal("document-definition.retired", audit.Action);
    }

    private static DocumentDefinition BuildDefinition(
        Guid hospitalId,
        string code,
        DocumentDefinitionStatus status)
    {
        return new DocumentDefinition
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            Code = code,
            Name = $"Name for {code}",
            Status = status,
            FormDefinitionId = Guid.NewGuid(),
            FormVersionId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            ClinicalAreaId = Guid.NewGuid(),
            DisciplineId = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now,
            RowVersion = 0u,
        };
    }

    private static FormDefinition BuildPublishedForm(
        Guid hospitalId,
        Guid formDefinitionId,
        Guid formVersionId)
    {
        return new FormDefinition
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
            },
        };
    }

    private static CreateDocumentDefinitionRequest NewRequest(
        ServiceHarness harness,
        string? code = null,
        string? name = null)
    {
        return new CreateDocumentDefinitionRequest(
            code ?? RequestConstants.StampedCode,
            name ?? "Initial document",
            harness.FormVersionId,
            harness.FacilityId,
            harness.ClinicalAreaId,
            harness.DisciplineId);
    }

    private sealed class ServiceHarness
    {
        private ServiceHarness(
            FakeDocumentCatalogRepository repository,
            FakeFormRepository formRepository,
            FakeClinicalTaxonomyRepository taxonomyRepository,
            RecordingUnitOfWork unitOfWork,
            RecordingAuditWriter auditWriter,
            FakeHospitalContext hospitalContext,
            FixedTimeProvider timeProvider,
            Guid formVersionId,
            Guid facilityId,
            Guid clinicalAreaId,
            Guid disciplineId)
        {
            Repository = repository;
            FormRepository = formRepository;
            TaxonomyRepository = taxonomyRepository;
            UnitOfWork = unitOfWork;
            AuditWriter = auditWriter;
            HospitalContext = hospitalContext;
            TimeProvider = timeProvider;
            FormVersionId = formVersionId;
            FacilityId = facilityId;
            ClinicalAreaId = clinicalAreaId;
            DisciplineId = disciplineId;
            Service = new DocumentCatalogService(
                repository,
                formRepository,
                taxonomyRepository,
                unitOfWork,
                auditWriter,
                new FakeWorkflowContext(hospitalContext, timeProvider),
                new FakeCapabilityGuard());
        }

        public FakeDocumentCatalogRepository Repository { get; }

        public FakeFormRepository FormRepository { get; }

        public FakeClinicalTaxonomyRepository TaxonomyRepository { get; }

        public RecordingUnitOfWork UnitOfWork { get; }

        public RecordingAuditWriter AuditWriter { get; }

        public FakeHospitalContext HospitalContext { get; }

        public FixedTimeProvider TimeProvider { get; }

        public Guid FormVersionId { get; }

        public Guid FacilityId { get; }

        public Guid ClinicalAreaId { get; }

        public Guid DisciplineId { get; }

        public DocumentCatalogService Service { get; }

        public Guid HospitalId => HospitalContext.HospitalId;

        public static ServiceHarness Create()
        {
            var hospitalId = Guid.NewGuid();
            var formDefinitionId = Guid.NewGuid();
            var formVersionId = Guid.NewGuid();
            var facilityId = Guid.NewGuid();
            var clinicalAreaId = Guid.NewGuid();
            var disciplineId = Guid.NewGuid();

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
                },
            });

            FakeClinicalTaxonomyRepository taxonomyRepository = new();
            taxonomyRepository.SeedFacility(new Facility
            {
                Id = facilityId,
                HospitalId = hospitalId,
                Code = "facility",
                Name = "Facility",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            taxonomyRepository.SeedClinicalArea(new ClinicalArea
            {
                Id = clinicalAreaId,
                HospitalId = hospitalId,
                Code = "area",
                Name = "Area",
                FacilityId = facilityId,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            taxonomyRepository.SeedDiscipline(new Discipline
            {
                Id = disciplineId,
                HospitalId = hospitalId,
                Code = "discipline",
                Name = "Discipline",
                ClinicalAreaId = clinicalAreaId,
                CreatedAt = Now,
                UpdatedAt = Now,
            });

            return new ServiceHarness(
                new FakeDocumentCatalogRepository(),
                formRepository,
                taxonomyRepository,
                new RecordingUnitOfWork(),
                new RecordingAuditWriter(),
                new FakeHospitalContext(hospitalId),
                new FixedTimeProvider(Now),
                formVersionId,
                facilityId,
                clinicalAreaId,
                disciplineId);
        }
    }

    private sealed record AuditEntryDto(
        string ResourceType,
        string Action,
        string? ActorId,
        DateTimeOffset OccurredAt,
        string? MetadataCode,
        uint? MetadataRowVersion)
    {
        public static AuditEntryDto From(RecordingAuditWriter.AuditEntry entry)
        {
            string? code = null;
            uint? rowVersion = null;
            Type metadataType = entry.Metadata.GetType();
            PropertyInfo? codeProperty = metadataType.GetProperty(
                "code",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (codeProperty?.GetValue(entry.Metadata) is string codeValue)
            {
                code = codeValue;
            }

            PropertyInfo? rowVersionProperty = metadataType.GetProperty(
                "rowVersion",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (rowVersionProperty is not null)
            {
                object? rowVersionValue = rowVersionProperty.GetValue(entry.Metadata);
                if (rowVersionValue is not null)
                {
                    rowVersion = Convert.ToUInt32(rowVersionValue, CultureInfo.InvariantCulture);
                }
            }

            return new AuditEntryDto(
                entry.ResourceType,
                entry.Action,
                entry.ActorId,
                entry.OccurredAt,
                code,
                rowVersion);
        }
    }

    private static class RequestConstants
    {
        public const string StampedCode = "unit-test-doc";
    }
}
