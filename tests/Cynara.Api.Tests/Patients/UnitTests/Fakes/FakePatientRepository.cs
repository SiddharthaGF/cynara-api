using Cynara.Application.Modules.Patients.Persistence;
using Cynara.Domain.Patients;

namespace Cynara.Api.Tests.Patients.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IPatientRepository"/> for unit tests that
/// need to exercise the patient workflow without the EF Core stack.
/// Seeded entries can be pre-populated to validate filtering and
/// behavioural edges (cross-tenant, soft-deleted, conflict) outside the
/// integration tests.
/// </summary>
public sealed class FakePatientRepository : IPatientRepository
{
    private readonly List<Patient> entries = [];

    private readonly List<Patient> added = [];

    public IReadOnlyList<Patient> Added => added;

    public IReadOnlyList<Patient> Entries => entries;

    public void Seed(params Patient[] patients)
    {
        ArgumentNullException.ThrowIfNull(patients);
        entries.AddRange(patients);
    }

    public Task<Patient?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        Patient? match = entries.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<Patient?> FindByNormalizedMrnAsync(
        Guid hospitalId,
        string normalizedMrn,
        CancellationToken cancellationToken)
    {
        Patient? match = entries.SingleOrDefault(
            item => item.HospitalId == hospitalId
                && string.Equals(
                    item.NormalizedMrn,
                    normalizedMrn,
                    StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public Task<PatientSearchPage> SearchAsync(
        Guid hospitalId,
        PatientSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IEnumerable<Patient> query = entries
            .Where(item => item.HospitalId == hospitalId);

        if (!criteria.IncludeDeleted)
        {
            query = query.Where(item => item.DeletedAt == null);
        }

        if (!string.IsNullOrEmpty(criteria.NormalizedMrn))
        {
            query = query.Where(
                item => string.Equals(
                    item.NormalizedMrn,
                    criteria.NormalizedMrn,
                    StringComparison.Ordinal));
        }

        if (!string.IsNullOrEmpty(criteria.NormalizedNationalId))
        {
            query = query.Where(
                item => string.Equals(
                    item.NormalizedNationalId,
                    criteria.NormalizedNationalId,
                    StringComparison.Ordinal));
        }

        if (criteria.NameTokens.Count > 0)
        {
            query = query.Where(item =>
            {
                string fullName = item.NormalizedGivenName + " " + item.NormalizedFamilyName;
                return criteria.NameTokens.All(token =>
                    fullName.Contains(token, StringComparison.Ordinal));
            });
        }

        Patient[] ordered =
        [
            .. query
                .OrderBy(item => item.NormalizedFamilyName, StringComparer.Ordinal)
                .ThenBy(item => item.NormalizedGivenName, StringComparer.Ordinal)
                .ThenBy(item => item.NormalizedMrn, StringComparer.Ordinal),
        ];
        int totalCount = ordered.Length;
        int skip = (page - 1) * pageSize;
        IReadOnlyList<Patient> items =
        [
            .. ordered.Skip(skip).Take(pageSize),
        ];
        return Task.FromResult(new PatientSearchPage(items, totalCount));
    }

    public void Add(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        added.Add(patient);
        entries.Add(patient);
    }
}
