using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.FormResponses;

public sealed class FormResponseRepository(CynaraDbContext dbContext)
    : IFormResponseRepository
{
    public async Task<FormVersion?> FindPublishedVersionAsync(
        string code,
        string version,
        CancellationToken cancellationToken)
    {
        FormDefinition? definition = await dbContext.FormDefinitions
            .AsNoTracking()
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(
                item => item.Code == code,
                cancellationToken).ConfigureAwait(false);

        return definition?.Versions.SingleOrDefault(
            item => item.Version == version
                && item.Status == FormVersionStatus.Published);
    }

    public void Add(
        FormResponse response,
        FormResponseRevision revision)
    {
        _ = dbContext.FormResponses.Add(response);
        _ = dbContext.FormResponseRevisions.Add(revision);
    }

    public void AddRevision(FormResponseRevision revision)
    {
        _ = dbContext.FormResponseRevisions.Add(revision);
    }

    public Task<FormResponse?> FindByIdAsync(
        Guid id,
        bool track,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        IQueryable<FormResponse> query = track
            ? dbContext.FormResponses
            : dbContext.FormResponses.AsNoTracking();

        if (includeDeleted)
        {
            query = query.IgnoreQueryFilters();
        }

        return query
            .Include(item => item.FormVersion)
            .ThenInclude(item => item.FormDefinition)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);
    }

    public Task<FormResponseRevision?> FindRevisionAsync(
        Guid responseId,
        uint revisionNumber,
        CancellationToken cancellationToken)
    {
        return dbContext.FormResponseRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.FormResponseId == responseId
                    && item.RevisionNumber == revisionNumber,
                cancellationToken);
    }

    public Task<List<FormResponseRevision>> ListRevisionsAsync(
        Guid responseId,
        CancellationToken cancellationToken)
    {
        return dbContext.FormResponseRevisions
            .AsNoTracking()
            .Where(item => item.FormResponseId == responseId)
            .OrderBy(item => item.RevisionNumber)
            .ToListAsync(cancellationToken);
    }

}
