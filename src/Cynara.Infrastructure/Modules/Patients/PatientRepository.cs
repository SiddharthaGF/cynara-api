using Cynara.Application.Modules.Patients.Persistence;
using Cynara.Domain.Patients;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Patients;

/// <summary>
/// EF Core implementation of the patient repository; all reads are
/// hospital-scoped, and soft-delete filtering is delegated to the
/// <c>PatientSearchCriteria</c> so application logic owns its semantics.
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

    public async Task<PatientSearchPage> SearchAsync(
        Guid hospitalId,
        PatientSearchCriteria criteria,
        int page,
        int pageSize,
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

        foreach (string token in criteria.NameTokens)
        {
            string nameToken = token;
            query = query.Where(
                item => (item.NormalizedGivenName + " " + item.NormalizedFamilyName)
                    .Contains(nameToken));
        }

        query = query
            .OrderBy(item => item.NormalizedFamilyName)
            .ThenBy(item => item.NormalizedGivenName)
            .ThenBy(item => item.NormalizedMrn);

        int totalCount = await query
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        int skip = (page - 1) * pageSize;
        IReadOnlyList<Patient> items = await query
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PatientSearchPage(items, totalCount);
    }

    public void Add(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        _ = dbContext.Patients.Add(patient);
    }
}
