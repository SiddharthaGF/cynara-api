using Cynara.Application.Modules.Encounters.Persistence;
using Cynara.Domain.Encounters;

namespace Cynara.Infrastructure.Modules.Encounters;

/// <summary>
/// EF Core implementation of the encounter repository. All reads are
/// hospital-scoped; tracked reads return tracked entities for workflow
/// mutations, and untracked reads are used for list projections.
/// </summary>
public sealed class EncounterRepository(CynaraDbContext dbContext)
    : IEncounterRepository
{
    public Task<Encounter?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Encounter> query = track
            ? dbContext.Encounters
            : dbContext.Encounters.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Encounter>> ListAsync(
        Guid hospitalId,
        EncounterListCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        IQueryable<Encounter> query = dbContext.Encounters
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (criteria.PatientId is Guid patientId)
        {
            query = query.Where(item => item.PatientId == patientId);
        }

        if (criteria.FacilityId is Guid facilityId)
        {
            query = query.Where(item => item.FacilityId == facilityId);
        }

        if (criteria.ClinicalAreaId is Guid clinicalAreaId)
        {
            query = query.Where(item => item.ClinicalAreaId == clinicalAreaId);
        }

        if (criteria.Status is EncounterStatus status)
        {
            query = query.Where(item => item.Status == status);
        }

        return await query
            .OrderByDescending(item => item.StartedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Encounter encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        _ = dbContext.Encounters.Add(encounter);
    }
}
