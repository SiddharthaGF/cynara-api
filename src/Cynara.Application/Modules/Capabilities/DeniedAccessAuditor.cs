using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Persistence;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Default denied-access auditor. Stages and commits an
/// <c>access.denied</c> audit event immediately; denials are raised before
/// any protected workflow mutates state. All failures are swallowed so the
/// auditor can never mask or change an authorization outcome.
/// </summary>
public sealed class DeniedAccessAuditor(
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider) : IDeniedAccessAuditor
{
    public async Task RecordAsync(
        string capability,
        string? actorId,
        string? requestPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);

        try
        {
            if (!hospitalContext.IsResolved)
            {
                return;
            }

            auditWriter.Append(
                AuditEntityTypes.Access,
                Guid.Empty,
                "access.denied",
                actorId,
                timeProvider.GetUtcNow(),
                new { capability, requestPath });

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
