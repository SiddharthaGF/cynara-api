using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy.Persistence;

/// <summary>
/// Persistence port for the clinical taxonomy aggregates. All read
/// operations are hospital-scoped; write operations return tracked
/// entities the workflows can mutate without committing.
/// </summary>
public interface IClinicalTaxonomyRepository
{
    public Task<Facility?> FindFacilityByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    public Task<Facility?> FindFacilityByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<Facility>> ListFacilitiesAsync(
        Guid hospitalId,
        bool includeRetired,
        CancellationToken cancellationToken);

    public Task<bool> FacilityCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken);

    public void AddFacility(Facility facility);

    public Task<ClinicalArea?> FindClinicalAreaByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    public Task<ClinicalArea?> FindClinicalAreaByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<ClinicalArea>> ListClinicalAreasAsync(
        Guid hospitalId,
        Guid? facilityId,
        bool includeRetired,
        CancellationToken cancellationToken);

    public Task<bool> ClinicalAreaCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken);

    public void AddClinicalArea(ClinicalArea clinicalArea);

    public Task<Discipline?> FindDisciplineByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    public Task<Discipline?> FindDisciplineByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<Discipline>> ListDisciplinesAsync(
        Guid hospitalId,
        Guid? clinicalAreaId,
        bool includeRetired,
        CancellationToken cancellationToken);

    public Task<bool> DisciplineCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken);

    public void AddDiscipline(Discipline discipline);
}
