using Cynara.Domain.Forms;

namespace Cynara.Application.Persistence;

public interface IFormRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    public Task AddDefinitionAsync(FormDefinition definition, FormVersion draft, CancellationToken cancellationToken);

    public Task<IReadOnlyList<FormDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken);

    public Task<FormDefinition?> FindDefinitionByCodeAsync(string code, bool track, CancellationToken cancellationToken);

    public Task AddVersionAsync(FormVersion version, CancellationToken cancellationToken);

    public void RemoveVersion(FormVersion version);

    public Task SaveChangesAsync(CancellationToken cancellationToken);
}
