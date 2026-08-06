using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Persistence;

namespace Cynara.Application.Audit;

/// <summary>
/// Default sensitive-read auditor. Stages a <c>*.read</c> audit event
/// through the current unit of work and commits it immediately, mirroring
/// the denied-access auditor. Reads are non-mutating workflows, so the
/// committed changes are limited to the audit event itself. The metadata
/// carries only the request path; the clinical payload is deliberately not
/// captured. All failures are swallowed so an audit write can never turn a
/// successful read into an error response.
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
            // Best-effort: a failed audit must never change the read outcome.
            _ = exception;
        }
    }
}
