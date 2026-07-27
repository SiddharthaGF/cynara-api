using Cynara.Application.Modules.Documents;
using Cynara.Domain.Documents;

namespace Cynara.Api.Tests.Documents.UnitTests;

/// <summary>
/// Unit coverage for the document catalog DTO mapper. The mapper is shared
/// between the list, create, update, and retire workflows, so its field
/// projection must stay byte-stable for the JSON:API contract.
/// </summary>
public sealed class DocumentCatalogMappersTests
{
    [Fact]
    public void ToDto_Throws_WhenDefinitionIsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DocumentCatalogMappers.ToDto(null!));
    }

    [Fact]
    public void ToDto_ProjectsEveryField()
    {
        DateTimeOffset createdAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        DateTimeOffset updatedAt = new(2026, 1, 3, 4, 5, 6, TimeSpan.Zero);
        DateTimeOffset retiredAt = new(2026, 1, 4, 5, 6, 7, TimeSpan.Zero);
        DocumentDefinition definition = Build(
            status: DocumentDefinitionStatus.Retired,
            createdAt: createdAt,
            updatedAt: updatedAt,
            retiredAt: retiredAt,
            rowVersion: 7u);

        DocumentDefinitionDto dto = DocumentCatalogMappers.ToDto(definition);

        Assert.Equal(definition.Id, dto.Id);
        Assert.Equal(definition.Code, dto.Code);
        Assert.Equal(definition.Name, dto.Name);
        Assert.Equal("retired", dto.Status);
        Assert.Equal(definition.FormDefinitionId, dto.FormDefinitionId);
        Assert.Equal(definition.FormVersionId, dto.FormVersionId);
        Assert.Equal(definition.FacilityId, dto.FacilityId);
        Assert.Equal(definition.ClinicalAreaId, dto.ClinicalAreaId);
        Assert.Equal(definition.DisciplineId, dto.DisciplineId);
        Assert.Equal(definition.AllowsMultipleInstancesPerEncounter, dto.AllowsMultipleInstancesPerEncounter);
        Assert.Equal(definition.RequiresActorForCreation, dto.RequiresActorForCreation);
        Assert.Equal(definition.RequiresActorForCompletion, dto.RequiresActorForCompletion);
        Assert.Equal(7u, dto.RowVersion);
        Assert.Equal(retiredAt, dto.RetiredAt);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Equal(updatedAt, dto.UpdatedAt);
    }

    [Fact]
    public void ToDto_ReturnsLowercaseStatus()
    {
        DocumentDefinition definition = Build(
            status: DocumentDefinitionStatus.Active,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            retiredAt: null,
            rowVersion: 0u);

        DocumentDefinitionDto dto = DocumentCatalogMappers.ToDto(definition);

        Assert.Equal("active", dto.Status);
    }

    [Fact]
    public void ToDto_NormalisesNullRetiredAt()
    {
        DocumentDefinition definition = Build(
            status: DocumentDefinitionStatus.Active,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            retiredAt: null,
            rowVersion: 0u);

        DocumentDefinitionDto dto = DocumentCatalogMappers.ToDto(definition);

        Assert.Null(dto.RetiredAt);
    }

    private static DocumentDefinition Build(
        DocumentDefinitionStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? retiredAt,
        uint rowVersion)
    {
        return new DocumentDefinition
        {
            Id = Guid.NewGuid(),
            HospitalId = Guid.NewGuid(),
            Code = "unit-test",
            Name = "Unit test",
            Status = status,
            FormDefinitionId = Guid.NewGuid(),
            FormVersionId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            ClinicalAreaId = Guid.NewGuid(),
            DisciplineId = Guid.NewGuid(),
            AllowsMultipleInstancesPerEncounter = false,
            RequiresActorForCreation = false,
            RequiresActorForCompletion = true,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            RetiredAt = retiredAt,
            RowVersion = rowVersion,
        };
    }
}
