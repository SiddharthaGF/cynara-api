namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Resolves the effective capability set for the current actor within the
/// resolved hospital workspace. Requests without an actor or hospital
/// context resolve to the empty set. Implementations are scoped per request
/// and may memoize so endpoint- and domain-level checks share one lookup.
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
