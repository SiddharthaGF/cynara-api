using Cynara.Application.Modules.Capabilities;

namespace Cynara.IdentitySpike.Auth;

/// <summary>
/// Principal-backed implementation of <see cref="ICurrentActor"/>. Returns
/// the actor identity resolved by the membership middleware from the
/// authenticated token's <c>sub</c> claim and the request hospital header,
/// replacing the production host's <c>X-Actor-Id</c> header source without
/// changing the Application layer contract.
/// </summary>
public sealed class PrincipalCurrentActor(ResolvedActor resolvedActor)
    : ICurrentActor
{
    /// <inheritdoc />
    public string? ActorId => resolvedActor.ActorId;
}
