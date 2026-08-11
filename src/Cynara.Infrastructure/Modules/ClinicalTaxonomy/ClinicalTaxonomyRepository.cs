using System.Linq.Expressions;

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
    public static IQueryable<T> ApplyRetiredFilter<T>(
        IQueryable<T> query,
        bool includeRetired)
        where T : class, IClinicalTaxonomyDefinition
    {
        return includeRetired
            ? query
            : query.Where(item => item.Status == ClinicalTaxonomyStatus.Active);
    }

    public Task<Facility?> FindFacilityByIdAsync(
        Guid hospitalId, Guid id, bool track, CancellationToken cancellationToken)
    {
        return FindByIdAsync<Facility>(hospitalId, id, track, cancellationToken);
    }

    public Task<Facility?> FindFacilityByCodeAsync(
        Guid hospitalId, string code, bool track, CancellationToken cancellationToken)
    {
        return FindByCodeAsync<Facility>(hospitalId, code, track, cancellationToken);
    }

    public Task<IReadOnlyList<Facility>> ListFacilitiesAsync(
        Guid hospitalId, bool includeRetired, CancellationToken cancellationToken)
    {
        return ListAsync<Facility>(
            hospitalId,
            includeRetired,
            parentFilter: null,
            cancellationToken);
    }

    public Task<bool> FacilityCodeExistsAsync(
        Guid hospitalId, string code, CancellationToken cancellationToken)
    {
        return CodeExistsAsync<Facility>(hospitalId, code, cancellationToken);
    }

    public void AddFacility(Facility facility)
    {
        Add(facility);
    }

    public Task<ClinicalArea?> FindClinicalAreaByIdAsync(
        Guid hospitalId, Guid id, bool track, CancellationToken cancellationToken)
    {
        return FindByIdAsync<ClinicalArea>(hospitalId, id, track, cancellationToken);
    }

    public Task<ClinicalArea?> FindClinicalAreaByCodeAsync(
        Guid hospitalId, string code, bool track, CancellationToken cancellationToken)
    {
        return FindByCodeAsync<ClinicalArea>(hospitalId, code, track, cancellationToken);
    }

    public Task<IReadOnlyList<ClinicalArea>> ListClinicalAreasAsync(
        Guid hospitalId,
        Guid? facilityId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        Expression<Func<ClinicalArea, bool>>? parentFilter =
            facilityId is Guid parentId
                ? item => item.FacilityId == parentId
                : null;

        return ListAsync(
            hospitalId,
            includeRetired,
            parentFilter,
            cancellationToken);
    }

    public Task<bool> ClinicalAreaCodeExistsAsync(
        Guid hospitalId, string code, CancellationToken cancellationToken)
    {
        return CodeExistsAsync<ClinicalArea>(hospitalId, code, cancellationToken);
    }

    public void AddClinicalArea(ClinicalArea clinicalArea)
    {
        Add(clinicalArea);
    }

    public Task<Discipline?> FindDisciplineByIdAsync(
        Guid hospitalId, Guid id, bool track, CancellationToken cancellationToken)
    {
        return FindByIdAsync<Discipline>(hospitalId, id, track, cancellationToken);
    }

    public Task<Discipline?> FindDisciplineByCodeAsync(
        Guid hospitalId, string code, bool track, CancellationToken cancellationToken)
    {
        return FindByCodeAsync<Discipline>(hospitalId, code, track, cancellationToken);
    }

    public Task<IReadOnlyList<Discipline>> ListDisciplinesAsync(
        Guid hospitalId,
        Guid? clinicalAreaId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        Expression<Func<Discipline, bool>>? parentFilter =
            clinicalAreaId is Guid parentId
                ? item => item.ClinicalAreaId == parentId
                : null;

        return ListAsync(
            hospitalId,
            includeRetired,
            parentFilter,
            cancellationToken);
    }

    public Task<bool> DisciplineCodeExistsAsync(
        Guid hospitalId, string code, CancellationToken cancellationToken)
    {
        return CodeExistsAsync<Discipline>(hospitalId, code, cancellationToken);
    }

    public void AddDiscipline(Discipline discipline)
    {
        Add(discipline);
    }

    private Task<T?> FindByIdAsync<T>(
        Guid hospitalId, Guid id, bool track, CancellationToken cancellationToken)
        where T : class, IClinicalTaxonomyDefinition
    {
        IQueryable<T> query = track
            ? dbContext.Set<T>()
            : dbContext.Set<T>().AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    private Task<T?> FindByCodeAsync<T>(
        Guid hospitalId, string code, bool track, CancellationToken cancellationToken)
        where T : class, IClinicalTaxonomyDefinition
    {
        IQueryable<T> query = track
            ? dbContext.Set<T>()
            : dbContext.Set<T>().AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        Guid hospitalId,
        bool includeRetired,
        Expression<Func<T, bool>>? parentFilter,
        CancellationToken cancellationToken)
        where T : class, IClinicalTaxonomyDefinition
    {
        IQueryable<T> query = dbContext.Set<T>()
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (parentFilter is not null)
        {
            query = query.Where(parentFilter);
        }

        query = ApplyRetiredFilter(query, includeRetired);

        return await query
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<bool> CodeExistsAsync<T>(
        Guid hospitalId, string code, CancellationToken cancellationToken)
        where T : class, IClinicalTaxonomyDefinition
    {
        return dbContext.Set<T>().AnyAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    private void Add<T>(T entity)
        where T : class, IClinicalTaxonomyDefinition
    {
        _ = dbContext.Set<T>().Add(entity);
    }
}
