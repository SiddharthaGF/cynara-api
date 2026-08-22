using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Forms;

public sealed class FormRepository(CynaraDbContext dbContext) : IFormRepository
{
    public Task<bool> CodeExistsAsync(
        string code,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return dbContext.FormDefinitions.AnyAsync(
            form => form.HospitalId == hospitalId && form.Code == code,
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
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return await dbContext.FormDefinitions
            .AsNoTracking()
            .Where(form => form.HospitalId == hospitalId)
            .Include(form => form.Versions)
            .OrderBy(form => form.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<FormDefinition?> FindDefinitionByCodeAsync(
        string code,
        Guid hospitalId,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<FormDefinition> query = track
            ? dbContext.FormDefinitions
            : dbContext.FormDefinitions.AsNoTracking();

        return query
            .Where(form => form.HospitalId == hospitalId && form.Code == code)
            .Include(form => form.Versions)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<FormVersion?> FindVersionByIdAsync(
        Guid hospitalId,
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        return dbContext.FormVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == formVersionId
                    && item.HospitalId == hospitalId,
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
