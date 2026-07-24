using Cynara.Application.Modules.Components.Persistence;
using Cynara.Domain.Components;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Components;

public sealed class ComponentRepository(CynaraDbContext dbContext) : IComponentRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return dbContext.ComponentDefinitions.AnyAsync(
            component => component.HospitalId == hospitalId && component.Code == code,
            cancellationToken);
    }

    public void AddDefinition(
        ComponentDefinition definition,
        ComponentVersion draft)
    {
        _ = dbContext.ComponentDefinitions.Add(definition);
        _ = dbContext.ComponentVersions.Add(draft);
    }

    public async Task<IReadOnlyList<ComponentDefinition>> ListDefinitionsAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ComponentDefinitions
            .AsNoTracking()
            .Where(component => component.HospitalId == hospitalId)
            .Include(component => component.Versions)
            .OrderBy(component => component.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ComponentDefinition?> FindDefinitionByCodeAsync(
        string code,
        Guid hospitalId,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ComponentDefinition> query = track
            ? dbContext.ComponentDefinitions
            : dbContext.ComponentDefinitions.AsNoTracking();

        return query
            .Where(component => component.HospitalId == hospitalId && component.Code == code)
            .Include(component => component.Versions)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ComponentVersion?> FindPublishedVersionAsync(
        string code,
        Guid hospitalId,
        string version,
        CancellationToken cancellationToken)
    {
        ComponentDefinition? definition = await dbContext.ComponentDefinitions
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId && item.Code == code)
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return definition?.Versions.SingleOrDefault(
            item => string.Equals(
                item.Version,
                version,
                StringComparison.Ordinal)
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
