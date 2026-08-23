using Cynara.Application.Modules.Workflows.Persistence;
using Cynara.Domain.Workflows;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Workflows;

/// <summary>
/// EF Core implementation of the pipeline repository; reads include the
/// pinned workflow version so mappers avoid round trips, and tracked reads
/// include history so transitions compute the next sequence in one trip.
/// </summary>
public sealed class PipelineRepository(CynaraDbContext dbContext) : IPipelineRepository
{
    public Task<Pipeline?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Pipeline> query = track
            ? dbContext.WorkflowPipelines
            : dbContext.WorkflowPipelines.AsNoTracking();
        query = query
            .Include(item => item.WorkflowVersion)
            .ThenInclude(item => item.WorkflowDefinition);
        if (track)
        {
            query = query.Include(item => item.History);
        }

        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Pipeline>> ListAsync(
        Guid hospitalId,
        PipelineListCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        IQueryable<Pipeline> query = dbContext.WorkflowPipelines
            .AsNoTracking()
            .Include(item => item.WorkflowVersion)
            .ThenInclude(item => item.WorkflowDefinition)
            .Where(item => item.HospitalId == hospitalId);

        ApplyFilters(ref query, criteria);

        return await query
            .OrderByDescending(item => item.StartedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Pipeline>> ListForJourneyAsync(
        Guid hospitalId,
        PipelineListCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        IQueryable<Pipeline> query = dbContext.WorkflowPipelines
            .AsNoTracking()
            .Include(item => item.WorkflowVersion)
            .ThenInclude(item => item.WorkflowDefinition)
            .Include(item => item.History)
            .Where(item => item.HospitalId == hospitalId);

        ApplyFilters(ref query, criteria);

        return await query
            .OrderByDescending(item => item.StartedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PipelineHistory>> ListHistoryAsync(
        Guid hospitalId,
        Guid pipelineId,
        CancellationToken cancellationToken)
    {
        return await dbContext.WorkflowPipelineHistory
            .AsNoTracking()
            .Where(item =>
                item.HospitalId == hospitalId
                && item.PipelineId == pipelineId)
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _ = dbContext.WorkflowPipelines.Add(pipeline);
    }

    private static void ApplyFilters(
        ref IQueryable<Pipeline> query,
        PipelineListCriteria criteria)
    {
        if (criteria.SubjectType is PipelineSubjectType subjectType)
        {
            query = query.Where(item => item.SubjectType == subjectType);
        }

        if (criteria.SubjectId is Guid subjectId)
        {
            query = query.Where(item => item.SubjectId == subjectId);
        }

        if (criteria.Status is PipelineStatus status)
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
    }
}
