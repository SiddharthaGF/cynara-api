using Cynara.Application.Modules.Capabilities;

namespace Cynara.Api.Common.ActorContext;

/// <summary>
/// Principal-backed implementation of <see cref="ICurrentActor"/>. Returns
/// the actor identity resolved by the membership middleware from the
/// authenticated token's <c>sub</c> claim and the resolved hospital workspace,
/// replacing the spoofable <c>X-Actor-Id</c> header source. It falls back to
/// the scoped <see cref="CurrentActorOverride"/> only outside HTTP requests
/// (bootstrap/preview seeding); production never reads <c>X-Actor-Id</c>.
/// </summary>
public sealed class PrincipalCurrentActor(
    ResolvedActor resolvedActor,
    CurrentActorOverride actorOverride) : ICurrentActor
{
    /// <inheritdoc />
    public string? ActorId => resolvedActor.ActorId ?? actorOverride.ActorId;
}
