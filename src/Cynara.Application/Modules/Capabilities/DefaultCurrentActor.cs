namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Default application-layer current actor. Returns the scoped
/// <see cref="CurrentActorOverride"/>, which is only set by out-of-request
/// flows (bootstrap seeding). Every normal request resolves a
/// <see langword="null"/> override and therefore an empty capability set
/// (deny by default) unless the Api host overrides this registration with its
/// header-backed implementation.
/// </summary>
public sealed class DefaultCurrentActor(
    CurrentActorOverride actorOverride) : ICurrentActor
{
    public string? ActorId => actorOverride.ActorId;
}
