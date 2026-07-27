using Cynara.Application.Modules.ClinicalTaxonomy.Persistence;
using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IClinicalTaxonomyRepository"/> limited to the
/// surface the document catalog workflow calls. The fake doesn't preserve
/// tracking semantics because the catalog workflows always pass
/// <c>track: false</c> for read paths.
/// </summary>
public sealed class FakeClinicalTaxonomyRepository : IClinicalTaxonomyRepository
{
    private readonly List<Facility> facilities = [];

    private readonly List<ClinicalArea> clinicalAreas = [];

    private readonly List<Discipline> disciplines = [];

    public IReadOnlyCollection<Facility> Facilities => facilities;

    public IReadOnlyCollection<ClinicalArea> ClinicalAreas => clinicalAreas;

    public IReadOnlyCollection<Discipline> Disciplines => disciplines;

    public void SeedFacility(Facility facility)
    {
        facilities.Add(facility);
    }

    public void SeedClinicalArea(ClinicalArea area)
    {
        clinicalAreas.Add(area);
    }

    public void SeedDiscipline(Discipline discipline)
    {
        disciplines.Add(discipline);
    }

    public Task<Facility?> FindFacilityByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        Facility? match = facilities.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<Facility?> FindFacilityByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        Facility? match = facilities.SingleOrDefault(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Facility>> ListFacilitiesAsync(
        Guid hospitalId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        var query = facilities
            .Where(item => item.HospitalId == hospitalId)
            .ToList();
        return Task.FromResult<IReadOnlyList<Facility>>(query);
    }

    public Task<bool> FacilityCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        bool exists = facilities.Exists(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(exists);
    }

    public void AddFacility(Facility facility)
    {
        facilities.Add(facility);
    }

    public Task<ClinicalArea?> FindClinicalAreaByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        ClinicalArea? match = clinicalAreas.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<ClinicalArea?> FindClinicalAreaByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        ClinicalArea? match = clinicalAreas.SingleOrDefault(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<ClinicalArea>> ListClinicalAreasAsync(
        Guid hospitalId,
        Guid? facilityId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        var query = clinicalAreas
            .Where(item => item.HospitalId == hospitalId
                && (facilityId is null || item.FacilityId == facilityId))
            .ToList();
        return Task.FromResult<IReadOnlyList<ClinicalArea>>(query);
    }

    public Task<bool> ClinicalAreaCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        bool exists = clinicalAreas.Exists(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(exists);
    }

    public void AddClinicalArea(ClinicalArea clinicalArea)
    {
        clinicalAreas.Add(clinicalArea);
    }

    public Task<Discipline?> FindDisciplineByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        Discipline? match = disciplines.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<Discipline?> FindDisciplineByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        Discipline? match = disciplines.SingleOrDefault(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Discipline>> ListDisciplinesAsync(
        Guid hospitalId,
        Guid? clinicalAreaId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        var query = disciplines
            .Where(item => item.HospitalId == hospitalId
                && (clinicalAreaId is null || item.ClinicalAreaId == clinicalAreaId))
            .ToList();
        return Task.FromResult<IReadOnlyList<Discipline>>(query);
    }

    public Task<bool> DisciplineCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        bool exists = disciplines.Exists(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(exists);
    }

    public void AddDiscipline(Discipline discipline)
    {
        disciplines.Add(discipline);
    }
}
