namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Persistence port that lists the hospitals a user belongs to through
/// their active memberships. The listing is tenant-exempt and capability
/// free: it never resolves a selected hospital and never requires or returns
/// an actor, so callers can enumerate hospital choices before choosing one.
/// </summary>
public interface IHospitalMembershipReader
{
    /// <summary>
    /// Returns the code/name membership pairs for <paramref name="userId"/>,
    /// or an empty collection when the user has no memberships.
    /// </summary>
    public Task<IReadOnlyList<HospitalMembershipDto>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
