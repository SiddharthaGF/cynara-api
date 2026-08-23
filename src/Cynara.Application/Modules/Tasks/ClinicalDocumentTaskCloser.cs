using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Tasks.Persistence;
using Cynara.Domain.Tasks;

namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Default <see cref="IClinicalDocumentTaskCloser"/> that completes open
/// tasks for a just-completed clinical document and stages
/// <c>task.completed</c> audit events in the caller's unit of work.
/// </summary>
public sealed class ClinicalDocumentTaskCloser(
    ITaskRepository tasks,
    IAuditWriter auditWriter) : IClinicalDocumentTaskCloser
{
    public async Task CloseOpenTasksForCompletedDocumentAsync(
        Guid hospitalId,
        Guid? encounterId,
        string formCode,
        Guid clinicalDocumentId,
        string? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicalTask> open = await tasks
            .ListOpenByFormCodeAsync(
                hospitalId,
                encounterId!.Value,
                formCode,
                track: true,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (ClinicalTask task in open)
        {
            ClinicalTaskLifecycle.Complete(task, actorId, now);
            auditWriter.Append(
                AuditEntityTypes.Task,
                task.Id,
                "task.completed",
                actorId,
                now,
                new
                {
                    clinicalDocumentId,
                    documentDefinitionCode = formCode,
                    pipelineId = task.PipelineId,
                    nodeId = task.NodeId,
                });
        }
    }
}
