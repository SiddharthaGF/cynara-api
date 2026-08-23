namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Default application-layer current actor: returns the scoped
/// <see cref="CurrentActorOverride"/>, set only by out-of-request flows.
/// Normal requests resolve a null override and thus an empty capability set
/// (deny by default) unless the Api host swaps in its header-backed actor.
/// </summary>
public sealed class DefaultCurrentActor(
    CurrentActorOverride actorOverride) : ICurrentActor
{
    public string? ActorId => actorOverride.ActorId;
}
