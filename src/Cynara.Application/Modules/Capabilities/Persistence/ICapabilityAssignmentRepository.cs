namespace Cynara.Application.Modules.Capabilities.Persistence;

/// <summary>
/// Persistence port for capability assignments. Implementations must keep
/// hospital-scoped queries bound to their hospital so an assignment in one
/// tenant can never resolve for another; platform-scoped rows authorize in
/// every hospital context and are the only cross-tenant surface.
/// </summary>
public interface ICapabilityAssignmentRepository
{
    /// <summary>
    /// Returns every capability code currently granted to
    /// <paramref name="actorId"/> for <paramref name="hospitalId"/>: the
    /// union of hospital-scoped grants assigned to that hospital and the
    /// actor's platform-scoped grants, which authorize in every hospital
    /// context.
    /// </summary>
    public Task<IReadOnlyList<string>> ListCapabilityCodesAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns all assignments for <paramref name="hospitalId"/>, newest
    /// first. Does not filter by actor so a tenant administrator can review
    /// the full grant surface.
    /// </summary>
    public Task<IReadOnlyList<Domain.Capabilities.CapabilityAssignment>> ListAsync(
        Guid hospitalId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the assignment for the actor/capability pair at
    /// <paramref name="scope"/>, if any. Hospital scope matches only the row
    /// assigned to <paramref name="hospitalId"/>; platform scope matches the
    /// single global row regardless of hospital.
    /// </summary>
    public Task<Domain.Capabilities.CapabilityAssignment?> FindAsync(
        Guid hospitalId,
        string actorId,
        string capability,
        string scope,
        bool track,
        CancellationToken cancellationToken);

    public void Add(Domain.Capabilities.CapabilityAssignment assignment);

    public void Remove(Domain.Capabilities.CapabilityAssignment assignment);
}
