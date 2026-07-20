using Cynara.Application.Persistence;
using Cynara.Domain.Audit;
using Cynara.Domain.Components;
using Cynara.Domain.Forms;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Persistence;

public sealed class ComponentRepository(CynaraDbContext dbContext) : IComponentRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        return dbContext.ComponentDefinitions.AnyAsync(component => component.Code == code, cancellationToken);
    }

    public async Task AddDefinitionAsync(ComponentDefinition definition, ComponentVersion draft, CancellationToken cancellationToken)
    {
        _ = dbContext.ComponentDefinitions.Add(definition);
        _ = dbContext.ComponentVersions.Add(draft);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ComponentDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ComponentDefinitions
            .AsNoTracking()
            .Include(component => component.Versions)
            .OrderBy(component => component.Code)
            .ToListAsync(cancellationToken);
    }

    public Task<ComponentDefinition?> FindDefinitionByCodeAsync(string code, bool track, CancellationToken cancellationToken)
    {
        IQueryable<ComponentDefinition> query = track
            ? dbContext.ComponentDefinitions
            : dbContext.ComponentDefinitions.AsNoTracking();

        return query
            .Include(component => component.Versions)
            .SingleOrDefaultAsync(component => component.Code == code, cancellationToken);
    }

    public async Task<ComponentVersion?> FindPublishedVersionAsync(
        string code,
        string version,
        CancellationToken cancellationToken)
    {
        ComponentDefinition? definition = await dbContext.ComponentDefinitions
            .AsNoTracking()
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken);

        return definition?.Versions.SingleOrDefault(
            item => item.Version == version
                && item.Status is ComponentVersionStatus.Published or ComponentVersionStatus.Retired);
    }

    public async Task AddVersionAsync(ComponentVersion version, CancellationToken cancellationToken)
    {
        _ = dbContext.ComponentVersions.Add(version);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
    }

    public void RemoveVersion(ComponentVersion version)
    {
        _ = dbContext.ComponentVersions.Remove(version);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class FormRepository(CynaraDbContext dbContext) : IFormRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        return dbContext.FormDefinitions.AnyAsync(form => form.Code == code, cancellationToken);
    }

    public async Task AddDefinitionAsync(FormDefinition definition, FormVersion draft, CancellationToken cancellationToken)
    {
        _ = dbContext.FormDefinitions.Add(definition);
        _ = dbContext.FormVersions.Add(draft);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FormDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.FormDefinitions
            .AsNoTracking()
            .Include(form => form.Versions)
            .OrderBy(form => form.Code)
            .ToListAsync(cancellationToken);
    }

    public Task<FormDefinition?> FindDefinitionByCodeAsync(string code, bool track, CancellationToken cancellationToken)
    {
        IQueryable<FormDefinition> query = track
            ? dbContext.FormDefinitions
            : dbContext.FormDefinitions.AsNoTracking();

        return query
            .Include(form => form.Versions)
            .SingleOrDefaultAsync(form => form.Code == code, cancellationToken);
    }

    public async Task AddVersionAsync(FormVersion version, CancellationToken cancellationToken)
    {
        _ = dbContext.FormVersions.Add(version);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
    }

    public void RemoveVersion(FormVersion version)
    {
        _ = dbContext.FormVersions.Remove(version);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class FormResponseRepository(CynaraDbContext dbContext) : IFormResponseRepository
{
    public async Task<FormVersion?> FindPublishedVersionAsync(
        string code,
        string version,
        CancellationToken cancellationToken)
    {
        FormDefinition? definition = await dbContext.FormDefinitions
            .AsNoTracking()
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken);

        return definition?.Versions.SingleOrDefault(
            item => item.Version == version && item.Status == FormVersionStatus.Published);
    }

    public async Task AddAsync(FormResponse response, FormResponseRevision revision, CancellationToken cancellationToken)
    {
        _ = dbContext.FormResponses.Add(response);
        _ = dbContext.FormResponseRevisions.Add(revision);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
    }

    public void AddRevision(FormResponseRevision revision)
    {
        _ = dbContext.FormResponseRevisions.Add(revision);
    }

    public Task<FormResponse?> FindByIdAsync(
        Guid id,
        bool track,
        bool includeDeleted,
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
            .Include(item => item.FormVersion)
            .ThenInclude(item => item.FormDefinition)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task<FormResponseRevision?> FindRevisionAsync(
        Guid responseId,
        uint revisionNumber,
        CancellationToken cancellationToken)
    {
        return dbContext.FormResponseRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.FormResponseId == responseId && item.RevisionNumber == revisionNumber,
                cancellationToken);
    }

    public async Task<IReadOnlyList<FormResponseRevision>> ListRevisionsAsync(
        Guid responseId,
        CancellationToken cancellationToken)
    {
        return await dbContext.FormResponseRevisions
            .AsNoTracking()
            .Where(item => item.FormResponseId == responseId)
            .OrderBy(item => item.RevisionNumber)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuditRepository(CynaraDbContext dbContext) : IAuditRepository
{
    public void Add(AuditEvent auditEvent)
    {
        _ = dbContext.AuditEvents.Add(auditEvent);
    }

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(
        string? resourceType,
        Guid? resourceId,
        string? actorId,
        int limit,
        CancellationToken cancellationToken)
    {
        IQueryable<AuditEvent> query = dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            query = query.Where(item => item.ResourceType == resourceType);
        }

        if (resourceId is not null)
        {
            query = query.Where(item => item.ResourceId == resourceId);
        }

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            query = query.Where(item => item.ActorId == actorId);
        }

        List<AuditEvent> items = await query.ToListAsync(cancellationToken);
        return [.. items
            .OrderByDescending(item => item.OccurredAt)
            .Take(limit)];
    }
}
