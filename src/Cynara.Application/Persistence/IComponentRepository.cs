using Cynara.Domain.Components;

namespace Cynara.Application.Persistence;

public interface IComponentRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    public Task AddDefinitionAsync(ComponentDefinition definition, ComponentVersion draft, CancellationToken cancellationToken);

    public Task<IReadOnlyList<ComponentDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken);

    public Task<ComponentDefinition?> FindDefinitionByCodeAsync(string code, bool track, CancellationToken cancellationToken);

    public Task<ComponentVersion?> FindPublishedVersionAsync(string code, string version, CancellationToken cancellationToken);

    public Task AddVersionAsync(ComponentVersion version, CancellationToken cancellationToken);

    public void RemoveVersion(ComponentVersion version);

    public Task SaveChangesAsync(CancellationToken cancellationToken);
}
