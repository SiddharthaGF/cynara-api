using Cynara.Application.Modules.Tasks.Persistence;
using Cynara.Domain.Tasks;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Tasks;

/// <summary>
/// EF Core implementation of the clinical task repository. Reads are
/// hospital-scoped and ordered for stable paging. Write operations return
/// tracked entities the workflows can mutate without committing.
/// </summary>
public sealed class TaskRepository(CynaraDbContext dbContext) : ITaskRepository
{
    public Task<ClinicalTask?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicalTask> query = track
            ? dbContext.ClinicalTasks
            : dbContext.ClinicalTasks.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicalTask>> ListAsync(
        Guid hospitalId,
        TaskListCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        IQueryable<ClinicalTask> query = dbContext.ClinicalTasks
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        ApplyFilters(ref query, criteria);

        return await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClinicalTask>> ListOpenByFormCodeAsync(
        Guid hospitalId,
        Guid encounterId,
        string formCode,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicalTask> query = track
            ? dbContext.ClinicalTasks
            : dbContext.ClinicalTasks.AsNoTracking();
        query = query.Where(item =>
            item.HospitalId == hospitalId
            && item.EncounterId == encounterId
            && item.FormCode == formCode
            && (item.Status == ClinicalTaskStatus.Open
                || item.Status == ClinicalTaskStatus.Claimed));

        return await query
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClinicalTask>> ListOpenByPipelineAsync(
        Guid hospitalId,
        Guid pipelineId,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicalTask> query = track
            ? dbContext.ClinicalTasks
            : dbContext.ClinicalTasks.AsNoTracking();
        query = query.Where(item =>
            item.HospitalId == hospitalId
            && item.PipelineId == pipelineId
            && (item.Status == ClinicalTaskStatus.Open
                || item.Status == ClinicalTaskStatus.Claimed));

        return await query
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(ClinicalTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _ = dbContext.ClinicalTasks.Add(task);
    }

    private static void ApplyFilters(
        ref IQueryable<ClinicalTask> query,
        TaskListCriteria criteria)
    {
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
            query = query.Where(item => item.AssignedActor == criteria.AssignedActor);
        }

        if (!string.IsNullOrWhiteSpace(criteria.AssignedRole))
        {
            query = query.Where(item => item.AssignedRole == criteria.AssignedRole);
        }

        if (!string.IsNullOrWhiteSpace(criteria.AssignedDiscipline))
        {
            query = query.Where(item =>
                item.AssignedDiscipline == criteria.AssignedDiscipline);
        }
    }
}
