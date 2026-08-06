namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Default domain-boundary guard. Deny-by-default: a missing actor identity,
/// an unresolved hospital context, or an unassigned capability all resolve to
/// a <see cref="CapabilityForbiddenException"/> without distinguishing the
/// cause on the wire.
/// </summary>
public sealed class CapabilityGuard(
    ICurrentActor currentActor,
    IEffectiveCapabilityResolver resolver) : ICapabilityGuard
{
    public async Task RequireAsync(
        string capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);

        bool granted = await resolver
            .HasCapabilityAsync(capability, cancellationToken)
            .ConfigureAwait(false);
        if (!granted)
        {
            throw new CapabilityForbiddenException(
                capability,
                currentActor.ActorId);
        }
    }
}
