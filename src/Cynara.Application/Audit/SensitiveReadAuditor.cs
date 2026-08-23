using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Persistence;

namespace Cynara.Application.Audit;

/// <summary>
/// Default sensitive-read auditor. Stages and commits a <c>*.read</c> audit
/// event immediately; metadata carries only the request path, never the
/// clinical payload. All failures are swallowed so an audit write can never
/// turn a successful read into an error response.
/// </summary>
public sealed class SensitiveReadAuditor(
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider) : ISensitiveReadAuditor
{
    public async Task RecordAsync(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId,
        string? requestPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            if (!hospitalContext.IsResolved)
            {
                return;
            }

            auditWriter.Append(
                resourceType,
                resourceId,
                action,
                actorId,
                timeProvider.GetUtcNow(),
                new { requestPath });

            _ = await unitOfWork.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            _ = exception;
        }
    }
}
