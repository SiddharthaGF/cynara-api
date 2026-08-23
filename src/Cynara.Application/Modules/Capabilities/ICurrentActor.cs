namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// The actor identity of the current request, implemented by the Api host
/// so capability resolution stays transport-independent. Null means no
/// actor identity, which resolves to an empty capability set (deny by
/// default).
/// </summary>
public interface ICurrentActor
{
    public string? ActorId { get; }
}
