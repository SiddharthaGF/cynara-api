namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Domain-operation boundary enforcement: services call
/// <see cref="RequireAsync"/> before a protected workflow so checks survive
/// even if a transport stops applying the endpoint filter. Denials surface
/// as <see cref="CapabilityForbiddenException"/>, audited there.
/// </summary>
public interface ICapabilityGuard
{
    public Task RequireAsync(
        string capability,
        CancellationToken cancellationToken);
}
