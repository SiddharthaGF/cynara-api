using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Domain.Documents;

namespace Cynara.Infrastructure.Modules.Documents;

/// <summary>
/// EF Core implementation of the clinical document catalog repository;
/// all reads are hospital-scoped, tracked for workflow mutations and
/// untracked for list projections.
/// </summary>
public sealed class DocumentCatalogRepository(
    CynaraDbContext dbContext) : IDocumentCatalogRepository
{
    public async Task<IReadOnlyList<DocumentDefinition>> ListAsync(
        Guid hospitalId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        IQueryable<DocumentDefinition> query = dbContext.DocumentDefinitions
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (!includeRetired)
        {
            query = query.Where(item => item.Status == DocumentDefinitionStatus.Active);
        }

        return await query
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<DocumentDefinition?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<DocumentDefinition> query = track
            ? dbContext.DocumentDefinitions
            : dbContext.DocumentDefinitions.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public Task<bool> CodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        return dbContext.DocumentDefinitions.AnyAsync(
            item => item.HospitalId == hospitalId && item.Code == code,
            cancellationToken);
    }

    public void Add(DocumentDefinition documentDefinition)
    {
        _ = dbContext.DocumentDefinitions.Add(documentDefinition);
    }
}
