using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Forms;

public sealed class FormRepository(CynaraDbContext dbContext) : IFormRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken)
    {
        return dbContext.FormDefinitions.AnyAsync(
            form => form.Code == code,
            cancellationToken);
    }

    public void AddDefinition(
        FormDefinition definition,
        FormVersion draft)
    {
        _ = dbContext.FormDefinitions.Add(definition);
        _ = dbContext.FormVersions.Add(draft);
    }

    public async Task<IReadOnlyList<FormDefinition>> ListDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.FormDefinitions
            .AsNoTracking()
            .Include(form => form.Versions)
            .OrderBy(form => form.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<FormDefinition?> FindDefinitionByCodeAsync(
        string code,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<FormDefinition> query = track
            ? dbContext.FormDefinitions
            : dbContext.FormDefinitions.AsNoTracking();

        return query
            .Include(form => form.Versions)
            .SingleOrDefaultAsync(
                form => form.Code == code,
                cancellationToken);
    }

    public void AddVersion(FormVersion version)
    {
        _ = dbContext.FormVersions.Add(version);
    }

    public void RemoveVersion(FormVersion version)
    {
        _ = dbContext.FormVersions.Remove(version);
    }

}
