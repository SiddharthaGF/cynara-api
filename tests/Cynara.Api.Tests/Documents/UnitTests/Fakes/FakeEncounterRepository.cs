using Cynara.Application.Modules.Encounters.Persistence;
using Cynara.Domain.Encounters;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IEncounterRepository"/> for unit tests that
/// need to exercise the document start workflow without the EF Core stack.
/// </summary>
public sealed class FakeEncounterRepository : IEncounterRepository
{
    private readonly List<Encounter> encounters = [];

    public IReadOnlyList<Encounter> Entries => encounters;

    public void Seed(params Encounter[] seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);
        encounters.AddRange(seeded);
    }

    public Task<Encounter?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        Encounter? match = encounters.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Encounter>> ListAsync(
        Guid hospitalId,
        EncounterListCriteria criteria,
        CancellationToken cancellationToken)
    {
        IEnumerable<Encounter> query = encounters
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

        return Task.FromResult<IReadOnlyList<Encounter>>([.. query]);
    }

    public void Add(Encounter encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        encounters.Add(encounter);
    }
}
