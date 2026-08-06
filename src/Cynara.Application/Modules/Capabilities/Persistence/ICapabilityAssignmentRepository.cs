namespace Cynara.Application.Modules.Capabilities.Persistence;

/// <summary>
/// Persistence port for capability assignments. Implementations must scope
/// every query by hospital so an assignment in one tenant can never resolve
/// for another.
/// </summary>
public interface ICapabilityAssignmentRepository
{
    /// <summary>
    /// Returns every capability code currently granted to
    /// <paramref name="actorId"/> within <paramref name="hospitalId"/>.
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

    /// <summary>Finds the unique assignment for the triple, if any.</summary>
    public Task<Domain.Capabilities.CapabilityAssignment?> FindAsync(
        Guid hospitalId,
        string actorId,
        string capability,
        bool track,
        CancellationToken cancellationToken);

    public void Add(Domain.Capabilities.CapabilityAssignment assignment);

    public void Remove(Domain.Capabilities.CapabilityAssignment assignment);
}
