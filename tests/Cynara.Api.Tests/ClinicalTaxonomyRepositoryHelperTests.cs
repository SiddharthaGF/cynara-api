using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Infrastructure.Modules.ClinicalTaxonomy;

namespace Cynara.Api.Tests;

/// <summary>
/// Locks the active-filter helper extracted from the clinical taxonomy
/// repository. Default listings must hide retired rows; explicit
/// <c>includeRetired=true</c> must surface every row regardless of
/// lifecycle status. The helper is exercised directly against
/// in-memory queryables for facilities, clinical areas, and disciplines
/// to enforce the shared behavior end-to-end.
/// </summary>
public sealed class ClinicalTaxonomyRepositoryHelperTests
{
    [Fact]
    public void ApplyRetiredFilter_Facilities_DefaultsToActiveOnly()
    {
        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            HospitalId = Guid.NewGuid(),
            Code = "active",
            Name = "Active",
            Status = ClinicalTaxonomyStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var retiredFacility = new Facility
        {
            Id = Guid.NewGuid(),
            HospitalId = facility.HospitalId,
            Code = "retired",
            Name = "Retired",
            Status = ClinicalTaxonomyStatus.Retired,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        List<Facility> facilities = [facility, retiredFacility];

        IReadOnlyList<Facility> active = [
            .. ClinicalTaxonomyRepository.ApplyRetiredFilter(
                facilities.AsQueryable(),
                includeRetired: false),
        ];
        IReadOnlyList<Facility> all = [
            .. ClinicalTaxonomyRepository.ApplyRetiredFilter(
                facilities.AsQueryable(),
                includeRetired: true),
        ];

        Assert.Single(active);
        Assert.Equal(facility.Id, active[0].Id);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, item => item.Id == retiredFacility.Id);
    }

    [Fact]
    public void ApplyRetiredFilter_ClinicalAreas_DefaultsToActiveOnly()
    {
        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            HospitalId = Guid.NewGuid(),
            Code = "main",
            Name = "Main",
            Status = ClinicalTaxonomyStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var active = new ClinicalArea
        {
            Id = Guid.NewGuid(),
            HospitalId = facility.HospitalId,
            Code = "active-area",
            Name = "Active area",
            Status = ClinicalTaxonomyStatus.Active,
            FacilityId = facility.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var retired = new ClinicalArea
        {
            Id = Guid.NewGuid(),
            HospitalId = facility.HospitalId,
            Code = "retired-area",
            Name = "Retired area",
            Status = ClinicalTaxonomyStatus.Retired,
            FacilityId = facility.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        List<ClinicalArea> areas = [active, retired];

        IReadOnlyList<ClinicalArea> filtered = [
            .. ClinicalTaxonomyRepository.ApplyRetiredFilter(
                areas.AsQueryable(),
                includeRetired: false),
        ];

        Assert.Single(filtered);
        Assert.Equal(active.Id, filtered[0].Id);
    }

    [Fact]
    public void ApplyRetiredFilter_Disciplines_DefaultsToActiveOnly()
    {
        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            HospitalId = Guid.NewGuid(),
            Code = "main",
            Name = "Main",
            Status = ClinicalTaxonomyStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var area = new ClinicalArea
        {
            Id = Guid.NewGuid(),
            HospitalId = facility.HospitalId,
            Code = "area",
            Name = "Area",
            Status = ClinicalTaxonomyStatus.Active,
            FacilityId = facility.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var active = new Discipline
        {
            Id = Guid.NewGuid(),
            HospitalId = facility.HospitalId,
            Code = "cardio",
            Name = "Cardiology",
            Status = ClinicalTaxonomyStatus.Active,
            ClinicalAreaId = area.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var retired = new Discipline
        {
            Id = Guid.NewGuid(),
            HospitalId = facility.HospitalId,
            Code = "neuro",
            Name = "Neurology",
            Status = ClinicalTaxonomyStatus.Retired,
            ClinicalAreaId = area.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        List<Discipline> disciplines = [active, retired];

        IReadOnlyList<Discipline> filtered = [
            .. ClinicalTaxonomyRepository.ApplyRetiredFilter(
                disciplines.AsQueryable(),
                includeRetired: false),
        ];

        Assert.Single(filtered);
        Assert.Equal(active.Id, filtered[0].Id);
    }
}
