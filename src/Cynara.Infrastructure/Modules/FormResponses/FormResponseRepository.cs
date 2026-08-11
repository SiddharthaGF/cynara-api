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
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        FormDefinition? definition = await dbContext.FormDefinitions
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId && item.Code == code)
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return definition?.Versions.SingleOrDefault(
            item => string.Equals(
                item.Version,
                version,
                StringComparison.Ordinal)
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
        Guid hospitalId,
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
            .Where(item => item.HospitalId == hospitalId)
            .Include(item => item.FormVersion)
            .ThenInclude(item => item.FormDefinition)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);
    }
}
