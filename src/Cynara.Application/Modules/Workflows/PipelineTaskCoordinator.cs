using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Tasks;
using Cynara.Application.Modules.Tasks.Persistence;
using Cynara.Domain.Tasks;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Owns the clinical-task side effects a pipeline drives: generating a task
/// when advancing into a task node and canceling outstanding tasks on
/// termination. Mutations and audit events stage on the current unit of
/// work; the coordinating workflow owns the commit.
/// </summary>
public sealed class PipelineTaskCoordinator(
    ITaskRepository tasks,
    IAuditWriter auditWriter)
{
    internal async Task CreateForNodeAsync(
        Pipeline pipeline,
        WorkflowNode node,
        string? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicalTask> open = await tasks
            .ListOpenByPipelineAsync(
                pipeline.HospitalId,
                pipeline.Id,
                track: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (open.Any(item => string.Equals(
                item.NodeId,
                node.Id,
                StringComparison.Ordinal)))
        {
            return;
        }

        var task = new ClinicalTask
        {
            Id = Guid.NewGuid(),
            HospitalId = pipeline.HospitalId,
            PipelineId = pipeline.Id,
            WorkflowVersionId = pipeline.WorkflowVersionId,
            WorkflowDefinitionId = pipeline.WorkflowVersion.WorkflowDefinitionId,
            NodeId = node.Id,
            Name = node.Name ?? node.Id,
            Description = node.Description,
            Status = ClinicalTaskStatus.Open,
            AssignedActor = node.Assignee?.Actor,
            AssignedRole = node.Assignee?.Role,
            AssignedDiscipline = node.Assignee?.Discipline,
            PatientId = pipeline.PatientId,
            EncounterId = pipeline.EncounterId,
            FormCode = node.FormCode,
            FormVersion = node.FormVersion,
            DueAt = node.DueDays is null ? null : now.AddDays(node.DueDays.Value),
            CreatedAt = now,
            UpdatedAt = now,
        };

        tasks.Add(task);
        auditWriter.Append(
            AuditEntityTypes.Task,
            task.Id,
            "task.generated",
            actorId,
            now,
            new
            {
                pipelineId = task.PipelineId,
                workflowVersionId = task.WorkflowVersionId,
                nodeId = task.NodeId,
                formCode = task.FormCode,
                formVersion = task.FormVersion,
                assignedActor = task.AssignedActor,
                assignedRole = task.AssignedRole,
                assignedDiscipline = task.AssignedDiscipline,
                dueAt = task.DueAt,
            },
            patientId: task.PatientId,
            encounterId: task.EncounterId,
            workflowDefinitionId: task.WorkflowDefinitionId);
    }

    internal async Task CancelOpenAsync(
        Pipeline pipeline,
        string? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicalTask> open = await tasks
            .ListOpenByPipelineAsync(
                pipeline.HospitalId,
                pipeline.Id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (ClinicalTask task in open)
        {
            ClinicalTaskLifecycle.Cancel(task, actorId, now);
            auditWriter.Append(
                AuditEntityTypes.Task,
                task.Id,
                "task.canceled",
                actorId,
                now,
                new
                {
                    reason = "Pipeline terminated",
                    pipelineId = pipeline.Id,
                    nodeId = task.NodeId,
                },
                patientId: task.PatientId,
                encounterId: task.EncounterId,
                workflowDefinitionId: task.WorkflowDefinitionId);
        }
    }
}
