namespace Cynara.Application.Failures;

public sealed record FailureRequestContext(
    string Method,
    string? Path,
    string? Query,
    string? ActorId,
    string? TraceId,
    Guid? HospitalId = null);
