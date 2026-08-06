namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// The actor identity of the current request. The Api host implements this
/// from the <c>X-Actor-Id</c> header; the application layer consumes it so
/// capability resolution stays independent of the transport. A
/// <see langword="null"/> value means the request carried no actor identity,
/// which resolves to an empty capability set (deny by default).
/// </summary>
public interface ICurrentActor
{
    public string? ActorId { get; }
}
