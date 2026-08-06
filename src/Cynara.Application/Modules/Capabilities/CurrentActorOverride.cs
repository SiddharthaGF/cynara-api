namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Scoped, mutable actor identity used by out-of-request flows such as
/// bootstrap seeding. The Api host's header-backed
/// <see cref="ICurrentActor"/> falls back to this value when no HTTP request
/// is in flight, and the default application-level actor reads it directly.
/// Request scopes always start with a <see langword="null"/> override, so the
/// header remains authoritative during normal traffic.
/// </summary>
public sealed class CurrentActorOverride
{
    public string? ActorId { get; set; }
}
