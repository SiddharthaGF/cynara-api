using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Documents;
using Cynara.Domain.Forms;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Modules.Documents;

namespace Cynara.Api.Tests.Documents.UnitTests;

/// <summary>
/// Integration coverage for <see cref="DocumentCatalogRepository"/>. The
/// repository is the only EF Core implementation of the catalog port; this
/// hit against the shared Postgres fixture pins tenant scoping, retired
/// filtering, and the unique index on
/// <c>(HospitalId, Code)</c> at the database boundary so the unit tests
/// can stay DB-agnostic.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class DocumentCatalogRepositoryTests : IDisposable
{
    public DocumentCatalogRepositoryTests(
        PostgreSqlDatabaseFixture database)
    {
        Factory = new CynaraTenantWebApplicationFactory(database.Settings);
        Scope = Factory.Services.CreateScope();
    }

    public void Dispose()
    {
        Scope.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyTenantEntriesOrderedByCode()
    {
        CynaraDbContext dbContext = Scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        DocumentCatalogRepository repository = new(dbContext);
        (DocumentCatalogFixture fixture, Guid otherHospitalId) = await SeedFixtureAsync(dbContext);
        SeedDefinition(dbContext, fixture.HospitalId, fixture, "zzz-last", DocumentDefinitionStatus.Active);
        SeedDefinition(dbContext, fixture.HospitalId, fixture, "aaa-first", DocumentDefinitionStatus.Retired);
        SeedDefinition(dbContext, otherHospitalId, fixture, "other-tenant", DocumentDefinitionStatus.Active);
        _ = await dbContext.SaveChangesAsync();

        var entries = await repository.ListAsync(
            fixture.HospitalId,
            includeRetired: true,
            CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Equal("aaa-first", entries[0].Code);
        Assert.Equal("zzz-last", entries[1].Code);
    }

    [Fact]
    public async Task ListAsync_HidesRetiredEntriesByDefault()
    {
        CynaraDbContext dbContext = Scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        DocumentCatalogRepository repository = new(dbContext);
        (DocumentCatalogFixture fixture, _) = await SeedFixtureAsync(dbContext);
        SeedDefinition(dbContext, fixture.HospitalId, fixture, "active", DocumentDefinitionStatus.Active);
        dbContext.DocumentDefinitions.Add(new DocumentDefinition
        {
            Id = Guid.NewGuid(),
            HospitalId = fixture.HospitalId,
            Code = "retired",
            Name = "Retired",
            Status = DocumentDefinitionStatus.Active,
            FormDefinitionId = fixture.FormDefinitionId,
            FormVersionId = fixture.FormVersionId,
            FacilityId = fixture.FacilityId,
            ClinicalAreaId = fixture.ClinicalAreaId,
            DisciplineId = fixture.DisciplineId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = 0u,
        });
        _ = await dbContext.SaveChangesAsync();

        DocumentDefinition retired = dbContext.DocumentDefinitions
            .Single(item => item.Code == "retired");
        retired.Status = DocumentDefinitionStatus.Retired;
        retired.RetiredAt = DateTimeOffset.UtcNow;
        _ = await dbContext.SaveChangesAsync();

        var entries = await repository.ListAsync(
            fixture.HospitalId,
            includeRetired: false,
            CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("active", entries[0].Code);
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNullForOtherTenant()
    {
        CynaraDbContext dbContext = Scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        DocumentCatalogRepository repository = new(dbContext);
        (DocumentCatalogFixture fixture, Guid otherHospitalId) = await SeedFixtureAsync(dbContext);
        SeedDefinition(dbContext, fixture.HospitalId, fixture, "owned", DocumentDefinitionStatus.Active);
        _ = await dbContext.SaveChangesAsync();

        DocumentDefinition owned = dbContext.DocumentDefinitions
            .Single(item => item.HospitalId == fixture.HospitalId);

        DocumentDefinition? result = await repository.FindByIdAsync(
            otherHospitalId,
            owned.Id,
            track: false,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CodeExistsAsync_DistinguishesTenantScope()
    {
        CynaraDbContext dbContext = Scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        DocumentCatalogRepository repository = new(dbContext);
        (DocumentCatalogFixture fixture, Guid otherHospitalId) = await SeedFixtureAsync(dbContext);
        SeedDefinition(dbContext, fixture.HospitalId, fixture, "shared-code", DocumentDefinitionStatus.Active);
        _ = await dbContext.SaveChangesAsync();

        bool mine = await repository.CodeExistsAsync(
            fixture.HospitalId,
            "shared-code",
            CancellationToken.None);
        bool otherTenant = await repository.CodeExistsAsync(
            otherHospitalId,
            "shared-code",
            CancellationToken.None);

        Assert.True(mine);
        Assert.False(otherTenant);
    }

    [Fact]
    public async Task Add_StagesAndPersistsDocumentDefinition()
    {
        CynaraDbContext dbContext = Scope.ServiceProvider.GetRequiredService<CynaraDbContext>();
        DocumentCatalogRepository repository = new(dbContext);
        (DocumentCatalogFixture fixture, _) = await SeedFixtureAsync(dbContext);
        DocumentDefinition definition = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = fixture.HospitalId,
            Code = "added-via-repo",
            Name = "Added via repo",
            Status = DocumentDefinitionStatus.Active,
            FormDefinitionId = fixture.FormDefinitionId,
            FormVersionId = fixture.FormVersionId,
            FacilityId = fixture.FacilityId,
            ClinicalAreaId = fixture.ClinicalAreaId,
            DisciplineId = fixture.DisciplineId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        repository.Add(definition);
        _ = await dbContext.SaveChangesAsync();

        DocumentDefinition? stored = await dbContext.DocumentDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == definition.Id);

        Assert.NotNull(stored);
        Assert.Equal("added-via-repo", stored.Code);
    }

    private CynaraTenantWebApplicationFactory Factory { get; }

    private IServiceScope Scope { get; }

    private static async Task<(DocumentCatalogFixture Fixture, Guid OtherHospitalId)> SeedFixtureAsync(
        CynaraDbContext dbContext)
    {
        var hospitalId = Guid.NewGuid();
        Hospital hospital = new()
        {
            Id = hospitalId,
            Code = $"H{hospitalId:N}"[..13],
            Name = "Repo test hospital",
            MetadataJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        Hospital otherHospital = new()
        {
            Id = Guid.NewGuid(),
            Code = $"O{Guid.NewGuid():N}"[..13],
            Name = "Other hospital",
            MetadataJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Hospitals.AddRange(hospital, otherHospital);
        _ = await dbContext.SaveChangesAsync();

        var facilityId = Guid.NewGuid();
        dbContext.Facilities.Add(new Facility
        {
            Id = facilityId,
            HospitalId = hospitalId,
            Code = "repo-facility",
            Name = "Repo facility",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var clinicalAreaId = Guid.NewGuid();
        dbContext.ClinicalAreas.Add(new ClinicalArea
        {
            Id = clinicalAreaId,
            HospitalId = hospitalId,
            Code = "repo-area",
            Name = "Repo area",
            FacilityId = facilityId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var disciplineId = Guid.NewGuid();
        dbContext.Disciplines.Add(new Discipline
        {
            Id = disciplineId,
            HospitalId = hospitalId,
            Code = "repo-discipline",
            Name = "Repo discipline",
            ClinicalAreaId = clinicalAreaId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var formDefinitionId = Guid.NewGuid();
        var formVersionId = Guid.NewGuid();
        dbContext.FormDefinitions.Add(new FormDefinition
        {
            Id = formDefinitionId,
            HospitalId = hospitalId,
            Code = "repo-form",
            Name = "Repo form",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
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
                    CreatedAt = DateTimeOffset.UtcNow,
                    PublishedAt = DateTimeOffset.UtcNow,
                },
            },
        });
        _ = await dbContext.SaveChangesAsync();

        return (
            new DocumentCatalogFixture(
                hospitalId,
                facilityId,
                clinicalAreaId,
                disciplineId,
                formDefinitionId,
                formVersionId),
            otherHospital.Id);
    }

    private static void SeedDefinition(
        CynaraDbContext dbContext,
        Guid hospitalId,
        DocumentCatalogFixture fixture,
        string code,
        DocumentDefinitionStatus status)
    {
        dbContext.DocumentDefinitions.Add(new DocumentDefinition
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            Code = code,
            Name = $"Name for {code}",
            Status = status,
            FormDefinitionId = fixture.FormDefinitionId,
            FormVersionId = fixture.FormVersionId,
            FacilityId = fixture.FacilityId,
            ClinicalAreaId = fixture.ClinicalAreaId,
            DisciplineId = fixture.DisciplineId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = 0u,
        });
    }

    private sealed record DocumentCatalogFixture(
        Guid HospitalId,
        Guid FacilityId,
        Guid ClinicalAreaId,
        Guid DisciplineId,
        Guid FormDefinitionId,
        Guid FormVersionId);
}
