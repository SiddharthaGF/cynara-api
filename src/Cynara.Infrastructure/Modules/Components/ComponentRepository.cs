using Cynara.Application.Modules.Components.Persistence;
using Cynara.Domain.Components;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Components;

public sealed class ComponentRepository(CynaraDbContext dbContext) : IComponentRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken)
    {
        return dbContext.ComponentDefinitions.AnyAsync(
            component => component.Code == code,
            cancellationToken);
    }

    public void AddDefinition(
        ComponentDefinition definition,
        ComponentVersion draft)
    {
        _ = dbContext.ComponentDefinitions.Add(definition);
        _ = dbContext.ComponentVersions.Add(draft);
    }

    public Task<List<ComponentDefinition>> ListDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        return dbContext.ComponentDefinitions
            .AsNoTracking()
            .Include(component => component.Versions)
            .OrderBy(component => component.Code)
            .ToListAsync(cancellationToken);
    }

    public Task<ComponentDefinition?> FindDefinitionByCodeAsync(
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ComponentDefinition> query = track
            ? dbContext.ComponentDefinitions
            : dbContext.ComponentDefinitions.AsNoTracking();

        return query
            .Include(component => component.Versions)
            .SingleOrDefaultAsync(
                component => component.Code == code,
                cancellationToken);
    }

    public async Task<ComponentVersion?> FindPublishedVersionAsync(
        string code,
        string version,
        CancellationToken cancellationToken)
    {
        ComponentDefinition? definition = await dbContext.ComponentDefinitions
            .AsNoTracking()
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(
                item => item.Code == code,
                cancellationToken).ConfigureAwait(false);

        return definition?.Versions.SingleOrDefault(
            item => item.Version == version
                && item.Status is ComponentVersionStatus.Published
                    or ComponentVersionStatus.Retired);
    }

    public void AddVersion(ComponentVersion version)
    {
        _ = dbContext.ComponentVersions.Add(version);
    }

    public void RemoveVersion(ComponentVersion version)
    {
        _ = dbContext.ComponentVersions.Remove(version);
    }

}
