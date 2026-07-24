using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components.Persistence;

public interface IComponentRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public void AddDefinition(ComponentDefinition definition, ComponentVersion draft);

    public Task<IReadOnlyList<ComponentDefinition>> ListDefinitionsAsync(
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task<ComponentDefinition?> FindDefinitionByCodeAsync(
        string code,
        Guid hospitalId,
        bool track,
        CancellationToken cancellationToken);

    public Task<ComponentVersion?> FindPublishedVersionAsync(
        string code,
        Guid hospitalId,
        string version,
        CancellationToken cancellationToken);

    public void AddVersion(ComponentVersion version);

    public void RemoveVersion(ComponentVersion version);
}
