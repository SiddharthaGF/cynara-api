namespace Cynara.Domain.Failures;

public sealed class FailureLog
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string ExceptionType { get; set; }

    public required string Message { get; set; }

    public string? StackTrace { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }

    public string? RequestQuery { get; set; }

    public int StatusCode { get; set; }

    public string? TraceId { get; set; }

    public string? ActorId { get; set; }

    public string? MetadataJson { get; set; }
}
