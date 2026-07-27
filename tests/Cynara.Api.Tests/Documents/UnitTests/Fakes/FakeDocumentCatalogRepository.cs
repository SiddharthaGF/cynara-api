using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Domain.Documents;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IDocumentCatalogRepository"/> for unit tests
/// that need to exercise the catalog workflow without the EF Core stack.
/// Seeded entries can be pre-populated to validate filtering and behavioural
/// edges (cross-tenant, retired, conflict) outside the integration tests.
/// </summary>
public sealed class FakeDocumentCatalogRepository : IDocumentCatalogRepository
{
    private readonly List<DocumentDefinition> entries = [];

    private readonly List<DocumentDefinition> added = [];

    public IReadOnlyList<DocumentDefinition> Added => added;

    public IReadOnlyList<DocumentDefinition> Entries => entries;

    public void Seed(params DocumentDefinition[] definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        entries.AddRange(definitions);
    }

    public Task<IReadOnlyList<DocumentDefinition>> ListAsync(
        Guid hospitalId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        IEnumerable<DocumentDefinition> query = entries
            .Where(item => item.HospitalId == hospitalId);
        if (!includeRetired)
        {
            query = query.Where(item => item.Status == DocumentDefinitionStatus.Active);
        }

        return Task.FromResult<IReadOnlyList<DocumentDefinition>>([.. query]);
    }

    public Task<DocumentDefinition?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        DocumentDefinition? match = entries.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<bool> CodeExistsAsync(
        Guid hospitalId,
        string code,
        CancellationToken cancellationToken)
    {
        bool exists = entries.Exists(
            item => item.HospitalId == hospitalId
                && string.Equals(item.Code, code, StringComparison.Ordinal));
        return Task.FromResult(exists);
    }

    public void Add(DocumentDefinition documentDefinition)
    {
        ArgumentNullException.ThrowIfNull(documentDefinition);
        added.Add(documentDefinition);
        entries.Add(documentDefinition);
    }
}
