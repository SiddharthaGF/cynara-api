using Cynara.Application.Modules.Tasks.Persistence;
using Cynara.Domain.Tasks;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="ITaskRepository"/> for unit tests that
/// exercise the clinical document workflow without the EF Core stack.
/// Open-by-form-code lookups reflect the current in-memory roster so the
/// document-completion close path can be asserted.
/// </summary>
public sealed class FakeTaskRepository : ITaskRepository
{
    private readonly List<ClinicalTask> tasks = [];

    private readonly List<ClinicalTask> added = [];

    public IReadOnlyList<ClinicalTask> Added => added;

    public void Seed(params ClinicalTask[] seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);
        tasks.AddRange(seeded);
    }

    public Task<ClinicalTask?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        ClinicalTask? match = tasks.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<ClinicalTask>> ListAsync(
        Guid hospitalId,
        TaskListCriteria criteria,
        CancellationToken cancellationToken)
    {
        IEnumerable<ClinicalTask> query = tasks
            .Where(item => item.HospitalId == hospitalId);

        if (criteria.Status is ClinicalTaskStatus status)
        {
            query = query.Where(item => item.Status == status);
        }

        if (criteria.PatientId is Guid patientId)
        {
            query = query.Where(item => item.PatientId == patientId);
        }

        if (criteria.EncounterId is Guid encounterId)
        {
            query = query.Where(item => item.EncounterId == encounterId);
        }

        if (criteria.PipelineId is Guid pipelineId)
        {
            query = query.Where(item => item.PipelineId == pipelineId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.AssignedActor))
        {
            query = query.Where(item => string.Equals(item.AssignedActor, criteria.AssignedActor, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(criteria.AssignedRole))
        {
            query = query.Where(item => string.Equals(item.AssignedRole, criteria.AssignedRole, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(criteria.AssignedDiscipline))
        {
            query = query.Where(item => string.Equals(item.AssignedDiscipline, criteria.AssignedDiscipline, StringComparison.Ordinal));
        }

        return Task.FromResult<IReadOnlyList<ClinicalTask>>([.. query]);
    }

    public Task<IReadOnlyList<ClinicalTask>> ListOpenByFormCodeAsync(
        Guid hospitalId,
        Guid encounterId,
        string formCode,
        bool track,
        CancellationToken cancellationToken)
    {
        ClinicalTask[] matches = [.. tasks.Where(
            item => item.HospitalId == hospitalId
                && item.EncounterId == encounterId
                && string.Equals(
                    item.FormCode,
                    formCode,
                    StringComparison.Ordinal)
                && (item.Status == ClinicalTaskStatus.Open
                    || item.Status == ClinicalTaskStatus.Claimed))];
        return Task.FromResult<IReadOnlyList<ClinicalTask>>(matches);
    }

    public Task<IReadOnlyList<ClinicalTask>> ListOpenByPipelineAsync(
        Guid hospitalId,
        Guid pipelineId,
        bool track,
        CancellationToken cancellationToken)
    {
        ClinicalTask[] matches = [.. tasks.Where(
            item => item.HospitalId == hospitalId
                && item.PipelineId == pipelineId
                && (item.Status == ClinicalTaskStatus.Open
                    || item.Status == ClinicalTaskStatus.Claimed))];
        return Task.FromResult<IReadOnlyList<ClinicalTask>>(matches);
    }

    public void Add(ClinicalTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        added.Add(task);
        tasks.Add(task);
    }
}
