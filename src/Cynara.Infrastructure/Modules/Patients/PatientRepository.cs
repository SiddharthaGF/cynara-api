using Cynara.Application.Modules.Patients.Persistence;
using Cynara.Domain.Patients;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Patients;

/// <summary>
/// EF Core implementation of the patient repository. All reads are
/// hospital-scoped; tracked reads return tracked entities for workflow
/// mutations, and untracked reads are used for list projections. Soft-
/// delete filtering is delegated to the <c>PatientSearchCriteria</c>
/// because the application layer is the single source of truth for the
/// soft-delete semantics.
/// </summary>
public sealed class PatientRepository(CynaraDbContext dbContext)
    : IPatientRepository
{
    public Task<Patient?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Patient> query = track
            ? dbContext.Patients
            : dbContext.Patients.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public Task<Patient?> FindByNormalizedMrnAsync(
        Guid hospitalId,
        string normalizedMrn,
        CancellationToken cancellationToken)
    {
        return dbContext.Patients
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.HospitalId == hospitalId
                    && item.NormalizedMrn == normalizedMrn,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Patient>> SearchAsync(
        Guid hospitalId,
        PatientSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        IQueryable<Patient> query = dbContext.Patients
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (!criteria.IncludeDeleted)
        {
            query = query.Where(item => item.DeletedAt == null);
        }

        if (!string.IsNullOrEmpty(criteria.NormalizedMrn))
        {
            query = query.Where(
                item => item.NormalizedMrn == criteria.NormalizedMrn);
        }

        if (!string.IsNullOrEmpty(criteria.NormalizedNationalId))
        {
            query = query.Where(
                item => item.NormalizedNationalId == criteria.NormalizedNationalId);
        }

        if (!string.IsNullOrEmpty(criteria.NormalizedGivenName))
        {
            query = query.Where(
                item => item.NormalizedGivenName == criteria.NormalizedGivenName);
        }

        if (!string.IsNullOrEmpty(criteria.NormalizedFamilyName))
        {
            query = query.Where(
                item => item.NormalizedFamilyName == criteria.NormalizedFamilyName);
        }

        return await query
            .OrderBy(item => item.NormalizedFamilyName)
            .ThenBy(item => item.NormalizedGivenName)
            .ThenBy(item => item.NormalizedMrn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        _ = dbContext.Patients.Add(patient);
    }
}
