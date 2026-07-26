using Cynara.Application.Modules.ClinicalTaxonomy.Persistence;
using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.ClinicalTaxonomy;

/// <summary>
/// EF Core implementation of the clinical taxonomy repository. All reads
/// are hospital-scoped; tracked reads return tracked entities for
/// workflow mutations, and untracked reads are used for projections.
/// </summary>
public sealed class ClinicalTaxonomyRepository(
    CynaraDbContext dbContext) : IClinicalTaxonomyRepository
{
    public Task<Facility?> FindFacilityByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Facility> query = track
            ? dbContext.Facilities
            : dbContext.Facilities.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public Task<Facility?> FindFacilityByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Facility> query = track
            ? dbContext.Facilities
            : dbContext.Facilities.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Facility>> ListFacilitiesAsync(
        Guid hospitalId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        IQueryable<Facility> query = dbContext.Facilities
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (!includeRetired)
        {
            query = query.Where(
                item => item.Status == ClinicalTaxonomyStatus.Active);
        }

        return await query
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> FacilityCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        return dbContext.Facilities.AnyAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    public void AddFacility(Facility facility)
    {
        _ = dbContext.Facilities.Add(facility);
    }

    public Task<ClinicalArea?> FindClinicalAreaByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicalArea> query = track
            ? dbContext.ClinicalAreas
            : dbContext.ClinicalAreas.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public Task<ClinicalArea?> FindClinicalAreaByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicalArea> query = track
            ? dbContext.ClinicalAreas
            : dbContext.ClinicalAreas.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicalArea>> ListClinicalAreasAsync(
        Guid hospitalId,
        Guid? facilityId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicalArea> query = dbContext.ClinicalAreas
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (facilityId is Guid parentId)
        {
            query = query.Where(item => item.FacilityId == parentId);
        }

        if (!includeRetired)
        {
            query = query.Where(
                item => item.Status == ClinicalTaxonomyStatus.Active);
        }

        return await query
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> ClinicalAreaCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        return dbContext.ClinicalAreas.AnyAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    public void AddClinicalArea(ClinicalArea clinicalArea)
    {
        _ = dbContext.ClinicalAreas.Add(clinicalArea);
    }

    public Task<Discipline?> FindDisciplineByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Discipline> query = track
            ? dbContext.Disciplines
            : dbContext.Disciplines.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public Task<Discipline?> FindDisciplineByCodeAsync(
        Guid hospitalId,
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Discipline> query = track
            ? dbContext.Disciplines
            : dbContext.Disciplines.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Discipline>> ListDisciplinesAsync(
        Guid hospitalId,
        Guid? clinicalAreaId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        IQueryable<Discipline> query = dbContext.Disciplines
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (clinicalAreaId is Guid parentId)
        {
            query = query.Where(item => item.ClinicalAreaId == parentId);
        }

        if (!includeRetired)
        {
            query = query.Where(
                item => item.Status == ClinicalTaxonomyStatus.Active);
        }

        return await query
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> DisciplineCodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        return dbContext.Disciplines.AnyAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    public void AddDiscipline(Discipline discipline)
    {
        _ = dbContext.Disciplines.Add(discipline);
    }
}
