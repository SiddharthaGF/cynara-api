namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Resolves the effective capability set for the current actor within the
/// resolved hospital workspace. Requests without an actor identity or without
/// a resolved hospital context resolve to the empty set. Implementations are
/// scoped per request and may memoize the resolution so endpoint-level and
/// domain-level enforcement share a single lookup.
/// </summary>
public interface IEffectiveCapabilityResolver
{
    /// <summary>
    /// Returns <see langword="true"/> when the current actor holds
    /// <paramref name="capability"/> in the resolved hospital workspace.
    /// </summary>
    public Task<bool> HasCapabilityAsync(
        string capability,
        CancellationToken cancellationToken);
}
