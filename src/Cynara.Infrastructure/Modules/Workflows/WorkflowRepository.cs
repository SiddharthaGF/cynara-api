using Cynara.Application.Modules.Workflows.Persistence;
using Cynara.Domain.Workflows;

namespace Cynara.Infrastructure.Modules.Workflows;

public sealed class WorkflowRepository(CynaraDbContext dbContext) : IWorkflowRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return dbContext.WorkflowDefinitions.AnyAsync(
            workflow => workflow.HospitalId == hospitalId && workflow.Code == code,
            cancellationToken);
    }

    public void AddDefinition(
        WorkflowDefinition definition,
        WorkflowVersion draft)
    {
        _ = dbContext.WorkflowDefinitions.Add(definition);
        _ = dbContext.WorkflowVersions.Add(draft);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListDefinitionsAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Where(workflow => workflow.HospitalId == hospitalId)
            .Include(workflow => workflow.Versions)
            .OrderBy(workflow => workflow.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<WorkflowDefinition?> FindDefinitionByCodeAsync(
        string code,
        Guid hospitalId,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<WorkflowDefinition> query = track
            ? dbContext.WorkflowDefinitions
            : dbContext.WorkflowDefinitions.AsNoTracking();

        return query
            .Where(workflow => workflow.HospitalId == hospitalId && workflow.Code == code)
            .Include(workflow => workflow.Versions)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<WorkflowVersion?> FindPublishedVersionAsync(
        string code,
        Guid hospitalId,
        string version,
        CancellationToken cancellationToken)
    {
        WorkflowDefinition? definition = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId && item.Code == code)
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return definition?.Versions.SingleOrDefault(
            item => string.Equals(
                item.Version,
                version,
                StringComparison.Ordinal)
                && item.Status is WorkflowVersionStatus.Published
                    or WorkflowVersionStatus.Retired);
    }

    public void AddVersion(WorkflowVersion version)
    {
        _ = dbContext.WorkflowVersions.Add(version);
    }

    public void RemoveVersion(WorkflowVersion version)
    {
        _ = dbContext.WorkflowVersions.Remove(version);
    }
}
