namespace Cynara.Application.Failures;

public interface IFailureLogWriter
{
    public Task RecordAsync(
        Exception exception,
        FailureRequestContext context,
        int statusCode,
        CancellationToken cancellationToken);
}
