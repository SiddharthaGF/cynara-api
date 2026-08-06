namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Tenant-aware lifecycle service for clinical document instances.
/// Implementations stamp ownership from <c>IHospitalContext</c>, reject
/// retired catalog entries and non-open encounters, preserve the exact
/// published form version captured at creation, enforce the catalog
/// multiplicity policy per encounter, and emit audit events through the
/// shared unit-of-work boundary.
/// </summary>
public interface IClinicalDocumentService
{
    /// <summary>
    /// Starts a new clinical document instance bound to the supplied catalog
    /// entry and encounter. Resolves the correct published form snapshot,
    /// creates the bound form response, and rejects single-instance catalog
    /// entries that already have a document for the encounter.
    /// </summary>
    public Task<ClinicalDocumentDto> StartAsync(
        StartClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the document instance matching the supplied identifier within
    /// the resolved hospital workspace. Terminal states remain queryable.
    /// </summary>
    public Task<ClinicalDocumentDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists document instances for the resolved hospital workspace. Terminal
    /// states remain included so historical records stay readable.
    /// </summary>
    public Task<IReadOnlyList<ClinicalDocumentDto>> ListAsync(
        ClinicalDocumentListRequest request,
        CancellationToken cancellationToken);
}
