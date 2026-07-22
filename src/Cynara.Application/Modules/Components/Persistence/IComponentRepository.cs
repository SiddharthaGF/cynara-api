using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components.Persistence;

public interface IComponentRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    public void AddDefinition(ComponentDefinition definition, ComponentVersion draft);

    public Task<List<ComponentDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken);

    public Task<ComponentDefinition?> FindDefinitionByCodeAsync(string code, bool track, CancellationToken cancellationToken);

    public Task<ComponentVersion?> FindPublishedVersionAsync(string code, string version, CancellationToken cancellationToken);

    public void AddVersion(ComponentVersion version);

    public void RemoveVersion(ComponentVersion version);

}
