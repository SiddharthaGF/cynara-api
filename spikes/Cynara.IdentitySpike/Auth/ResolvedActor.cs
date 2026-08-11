namespace Cynara.IdentitySpike.Auth;

/// <summary>
/// Scoped holder for the actor identity resolved from the authenticated
/// user's membership. The middleware populates it per request; the
/// <c>PrincipalCurrentActor</c> reads it so the Application layer keeps
/// consuming the same <c>ICurrentActor</c> abstraction.
/// </summary>
public sealed class ResolvedActor
{
    /// <summary>The actor identity resolved for the current request.</summary>
    public string? ActorId { get; set; }
}
