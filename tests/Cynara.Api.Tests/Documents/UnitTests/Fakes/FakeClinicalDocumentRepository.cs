using Cynara.Application.Modules.Documents.Persistence;
using Cynara.Domain.Documents;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IClinicalDocumentRepository"/> for unit
/// tests that exercise the document start workflow without the EF Core
/// stack. Existence checks reflect the current in-memory roster so the
/// single-instance policy can be asserted.
/// </summary>
public sealed class FakeClinicalDocumentRepository
    : IClinicalDocumentRepository
{
    private readonly List<ClinicalDocument> documents = [];

    private readonly List<ClinicalDocument> added = [];

    public IReadOnlyList<ClinicalDocument> Added => added;

    public void Seed(params ClinicalDocument[] seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);
        documents.AddRange(seeded);
    }

    public Task<ClinicalDocument?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        ClinicalDocument? match = documents.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<ClinicalDocument>> ListAsync(
        Guid hospitalId,
        ClinicalDocumentListCriteria criteria,
        CancellationToken cancellationToken)
    {
        IEnumerable<ClinicalDocument> query = documents
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

        return Task.FromResult<IReadOnlyList<ClinicalDocument>>([.. query]);
    }

    public Task<bool> AnyInstanceExistsAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        bool exists = documents.Exists(
            item => item.HospitalId == hospitalId
                && item.DocumentDefinitionId == documentDefinitionId
                && item.EncounterId == encounterId);
        return Task.FromResult(exists);
    }

    public void Add(ClinicalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        added.Add(document);
        documents.Add(document);
    }
}
