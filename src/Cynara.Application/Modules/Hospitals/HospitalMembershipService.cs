namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Application-layer membership listing workflow consumed by the API host.
/// Applies a stable, code-ordered projection over
/// <see cref="IHospitalMembershipReader"/>. No tenant or capability gates
/// apply: the listing is deliberately bearer-only.
/// </summary>
public sealed class HospitalMembershipService(
    IHospitalMembershipReader memberships)
{
    /// <summary>
    /// Returns the caller's hospital choices ordered by code, or an empty
    /// collection when the caller has no memberships.
    /// </summary>
    public async Task<IReadOnlyList<HospitalMembershipDto>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HospitalMembershipDto> items = await memberships
            .ListAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        return [.. items.OrderBy(
            item => item.Code,
            StringComparer.Ordinal)];
    }
}
