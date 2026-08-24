using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Domain.Documents;

namespace Cynara.Infrastructure.Modules.Documents;

/// <summary>
/// EF Core implementation of the clinical document repository; all reads
/// are hospital-scoped, tracked for workflow mutations and untracked for
/// list projections. The existence check supports start workflows.
/// </summary>
public sealed class ClinicalDocumentRepository(CynaraDbContext dbContext)
    : IClinicalDocumentRepository
{
    public Task<ClinicalDocument?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicalDocument> query = track
            ? dbContext.ClinicalDocuments
            : dbContext.ClinicalDocuments.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id && item.HospitalId == hospitalId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicalDocument>> ListAsync(
        Guid hospitalId,
        ClinicalDocumentListCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        IQueryable<ClinicalDocument> query = dbContext.ClinicalDocuments
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId);

        if (criteria.EncounterId is Guid encounterId)
        {
            query = query.Where(item => item.EncounterId == encounterId);
        }

        if (criteria.PatientId is Guid patientId)
        {
            query = query.Where(item => item.PatientId == patientId);
        }

        if (criteria.DocumentDefinitionId is Guid documentDefinitionId)
        {
            query = query.Where(
                item => item.DocumentDefinitionId == documentDefinitionId);
        }

        if (criteria.Status is ClinicalDocumentStatus status)
        {
            query = query.Where(item => item.Status == status);
        }

        return await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> AnyInstanceExistsAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        return dbContext.ClinicalDocuments.AsNoTracking().AnyAsync(
            item => item.HospitalId == hospitalId
                && item.DocumentDefinitionId == documentDefinitionId
                && item.EncounterId == encounterId,
            cancellationToken);
    }

    public void Add(ClinicalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = dbContext.ClinicalDocuments.Add(document);
    }
}
