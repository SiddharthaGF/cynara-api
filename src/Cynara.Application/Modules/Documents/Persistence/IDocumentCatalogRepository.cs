using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents.Persistence;

/// <summary>
/// Persistence port for the clinical document catalog. All read operations
/// are hospital-scoped; write operations return tracked entities the
/// workflows can mutate without committing. The unit-of-work boundary is
/// owned by the workflow, not by the repository.
/// </summary>
public interface IDocumentCatalogRepository
{
    public Task<IReadOnlyList<DocumentDefinition>> ListAsync(
        Guid hospitalId,
        bool includeRetired,
        CancellationToken cancellationToken);

    public Task<DocumentDefinition?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    public Task<bool> CodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken);

    public void Add(DocumentDefinition documentDefinition);
}
