using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Application.Modules.Hospitals;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Scoped, memoized resolver of the current actor's effective capabilities.
/// The resolution is cached on the instance so the endpoint-level
/// authorization filter, the domain-level <see cref="CapabilityGuard"/>, and
/// the <c>GET /api/me/capabilities</c> endpoint all reuse a single query per
/// request. Cross-tenant leakage is structurally impossible: every lookup
/// filters by the resolved <see cref="IHospitalContext.HospitalId"/>.
/// </summary>
public sealed class EffectiveCapabilityResolver(
    ICurrentActor currentActor,
    IHospitalContext hospitalContext,
    ICapabilityAssignmentRepository repository)
    : IEffectiveCapabilityResolver
{
    private IReadOnlySet<string>? cached;

    public async Task<bool> HasCapabilityAsync(
        string capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        IReadOnlySet<string> effective = await ResolveAsync(cancellationToken)
            .ConfigureAwait(false);
        return effective.Contains(capability);
    }

    /// <summary>
    /// Resolves and caches the full effective capability set for the current
    /// request. Used by the current-user capabilities endpoint.
    /// </summary>
    public async Task<IReadOnlySet<string>> ResolveAsync(
        CancellationToken cancellationToken)
    {
        if (cached is not null)
        {
            return cached;
        }

        string? actorId = currentActor.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) || !hospitalContext.IsResolved)
        {
            cached = new HashSet<string>(StringComparer.Ordinal);
            return cached;
        }

        IReadOnlyList<string> codes = await repository
            .ListCapabilityCodesAsync(
                hospitalContext.HospitalId,
                actorId,
                cancellationToken)
            .ConfigureAwait(false);
        cached = codes.ToHashSet(StringComparer.Ordinal);
        return cached;
    }
}
