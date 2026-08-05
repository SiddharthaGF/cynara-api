namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Tenant-aware lifecycle service for clinical encounters.
/// Implementations stamp ownership from <c>IHospitalContext</c>, reject
/// cross-tenant or retired references, honor optimistic concurrency on
/// transitions, and emit audit events through the shared unit-of-work
/// boundary.
/// </summary>
public interface IEncounterService
{
    /// <summary>Creates a new open encounter under the resolved hospital.</summary>
    public Task<EncounterDto> CreateAsync(
        CreateEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the encounter matching the supplied identifier within the
    /// resolved hospital workspace. Terminal states remain queryable.
    /// </summary>
    public Task<EncounterDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists encounters for the resolved hospital workspace. Terminal
    /// states remain included so historical records stay readable.
    /// </summary>
    public Task<IReadOnlyList<EncounterDto>> ListAsync(
        EncounterListRequest request,
        CancellationToken cancellationToken);

    /// <summary>Completes an open encounter.</summary>
    public Task<EncounterDto> CompleteAsync(
        Guid id,
        TransitionEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Cancels an open encounter.</summary>
    public Task<EncounterDto> CancelAsync(
        Guid id,
        TransitionEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks an open encounter as entered-in-error. The record remains
    /// historically queryable.
    /// </summary>
    public Task<EncounterDto> EnterInErrorAsync(
        Guid id,
        TransitionEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
