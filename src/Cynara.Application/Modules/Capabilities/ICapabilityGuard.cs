namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Domain-operation boundary enforcement. Services inject this port and call
/// <see cref="RequireAsync"/> before executing a protected workflow so that
/// capability checks survive even if a future transport stops applying the
/// endpoint-level authorization filter. The denial surfaces as
/// <see cref="CapabilityForbiddenException"/> and is audited by the exception
/// handler, which is the single choke point for denied-access audit events.
/// </summary>
public interface ICapabilityGuard
{
    public Task RequireAsync(
        string capability,
        CancellationToken cancellationToken);
}
