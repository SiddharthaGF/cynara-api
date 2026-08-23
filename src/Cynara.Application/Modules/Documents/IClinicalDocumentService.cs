namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Tenant-aware lifecycle service for clinical document instances.
/// Implementations stamp ownership from <c>IHospitalContext</c>, reject
/// retired entries and non-open encounters, pin the form version, enforce
/// concurrency, and audit in one unit of work. Completed docs are immutable.
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

    /// <summary>
    /// Completes an in-progress document. The bound form response is
    /// validated in complete mode and completed in the same transaction, so
    /// the recorded content becomes immutable.
    /// </summary>
    public Task<ClinicalDocumentDto> CompleteAsync(
        Guid id,
        TransitionClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Cancels an in-progress document.</summary>
    public Task<ClinicalDocumentDto> CancelAsync(
        Guid id,
        TransitionClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks an in-progress or completed document as entered-in-error. The
    /// record remains historically queryable with its reason, actor, and
    /// timestamp.
    /// </summary>
    public Task<ClinicalDocumentDto> EnterInErrorAsync(
        Guid id,
        TransitionClinicalDocumentRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
