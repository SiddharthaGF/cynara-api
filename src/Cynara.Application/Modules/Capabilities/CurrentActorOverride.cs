namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Scoped, mutable actor identity for out-of-request flows (e.g. bootstrap
/// seeding). The Api host's header-backed <see cref="ICurrentActor"/> falls
/// back to this value when no HTTP request is in flight; request scopes
/// always start null so the header stays authoritative during traffic.
/// </summary>
public sealed class CurrentActorOverride
{
    public string? ActorId { get; set; }
}
