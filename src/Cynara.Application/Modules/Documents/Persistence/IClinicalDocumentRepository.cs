using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents.Persistence;

/// <summary>
/// Persistence port for clinical document instances. All read operations
/// are hospital-scoped; write operations return tracked entities the
/// workflows can mutate without committing. The unit-of-work boundary is
/// owned by the workflow, not by the repository.
/// </summary>
public interface IClinicalDocumentRepository
{
    /// <summary>
    /// Returns the document instance matching the supplied identifier in the
    /// resolved hospital workspace, or <see langword="null"/> when no record
    /// exists. Terminal states remain resolvable.
    /// </summary>
    public Task<ClinicalDocument?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists document instances in the resolved hospital workspace that
    /// match the supplied filter.
    /// </summary>
    public Task<IReadOnlyList<ClinicalDocument>> ListAsync(
        Guid hospitalId,
        ClinicalDocumentListCriteria criteria,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether any document instance already exists for the catalog
    /// entry and encounter pair. Used by the start workflow to enforce the
    /// single-instance-per-encounter policy.
    /// </summary>
    public Task<bool> AnyInstanceExistsAsync(
        Guid hospitalId,
        Guid documentDefinitionId,
        Guid encounterId,
        CancellationToken cancellationToken);

    /// <summary>Adds a new document instance to the change tracker.</summary>
    public void Add(ClinicalDocument document);
}

/// <summary>
/// Filter criteria for the document list endpoint. All fields are optional;
/// a fully empty criteria returns the hospital roster.
/// </summary>
public sealed record ClinicalDocumentListCriteria(
    Guid? EncounterId,
    Guid? PatientId,
    Guid? DocumentDefinitionId,
    ClinicalDocumentStatus? Status);
