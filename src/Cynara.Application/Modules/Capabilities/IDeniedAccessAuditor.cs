namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Records denied-access audit events. Exception handlers invoke this when
/// a <see cref="CapabilityForbiddenException"/> reaches the wire so every
/// denial is attributable. The implementation must never throw: a failing
/// audit must not change the authorization outcome.
/// </summary>
public interface IDeniedAccessAuditor
{
    public Task RecordAsync(
        string capability,
        string? actorId,
        string? requestPath,
        CancellationToken cancellationToken);
}
