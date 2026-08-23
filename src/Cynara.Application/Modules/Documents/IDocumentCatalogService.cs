namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Tenant-aware lifecycle service for the clinical document catalog.
/// Implementations stamp ownership from <c>IHospitalContext</c>, enforce
/// published-only form versions, validate taxonomy references against the
/// resolved hospital, and emit audit events in the shared boundary.
/// </summary>
public interface IDocumentCatalogService
{
    /// <summary>Lists document catalog entries for the resolved hospital.</summary>
    public Task<IReadOnlyList<DocumentDefinitionDto>> ListAsync(
        bool includeRetired,
        CancellationToken cancellationToken);

    /// <summary>Creates a new document catalog entry under the resolved hospital.</summary>
    public Task<DocumentDefinitionDto> CreateAsync(
        CreateDocumentDefinitionRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Updates mutable display and policy fields on an existing entry.</summary>
    public Task<DocumentDefinitionDto> UpdateAsync(
        Guid id,
        UpdateDocumentDefinitionRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retires a catalog entry. The pinned <c>FormVersionId</c> snapshot is
    /// preserved so historical documents remain resolvable.
    /// </summary>
    public Task<DocumentDefinitionDto> RetireAsync(
        Guid id,
        RetireDocumentDefinitionRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
