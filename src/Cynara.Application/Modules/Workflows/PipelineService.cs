using System.Text.Json;

using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Encounters;
using Cynara.Application.Modules.Encounters.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Patients.Persistence;
using Cynara.Application.Modules.Tasks;
using Cynara.Application.Modules.Tasks.Persistence;
using Cynara.Application.Modules.Workflows.Persistence;
using Cynara.Application.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Encounters;
using Cynara.Domain.Patients;
using Cynara.Domain.Tasks;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Default implementation of <see cref="IPipelineService"/>. Starting a
/// pipeline pins the exact published workflow version (read from the
/// immutable schema at every transition), validates the subject record in
/// the resolved hospital, advance evaluates decision conditions server-side
/// and moves the cursor along the graph, and the lifecycle operations
/// complete, cancel, or enter a pipeline in error. Every transition appends
/// to the immutable progression history and its audit event commits in the
/// same unit-of-work boundary.
/// </summary>
public sealed class PipelineService(
    IPipelineRepository pipelines,
    IWorkflowRepository workflows,
    IEncounterRepository encounters,
    IPatientRepository patients,
    ITaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    ICapabilityGuard capabilityGuard) : IPipelineService
{
    /// <inheritdoc />
    public async Task<PipelineDto> StartAsync(
        StartPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesWrite, cancellationToken)
            .ConfigureAwait(false);

        PipelineSubjectType subjectType = PipelineWorkflowHelpers.ParseSubjectType(
            request.SubjectType);
        WorkflowVersion version = await RequirePublishedVersionAsync(
                request.WorkflowCode,
                request.WorkflowVersion,
                cancellationToken)
            .ConfigureAwait(false);
        WorkflowGraph graph = WorkflowGraphReader.Read(version.WorkflowSchemaJson);

        PipelineSubjectBinding subject = subjectType switch
        {
            PipelineSubjectType.Encounter => await RequireActiveEncounterAsync(
                    request.SubjectId, cancellationToken)
                .ConfigureAwait(false),
            PipelineSubjectType.Patient => await RequireActivePatientAsync(
                    request.SubjectId, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Unknown pipeline subject type '{subjectType}'."),
        };

        DateTimeOffset now = timeProvider.GetUtcNow();
        var pipeline = new Pipeline
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            WorkflowVersionId = version.Id,
            WorkflowVersion = version,
            SubjectType = subjectType,
            SubjectId = request.SubjectId,
            PatientId = subject.PatientId,
            EncounterId = subject.EncounterId,
            Status = PipelineStatus.Running,
            CurrentNodeId = graph.StartNode.Id,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        PipelineMappers.AppendHistory(
            pipeline,
            actorId,
            now,
            "pipeline.started",
            new
            {
                workflowCode = request.WorkflowCode,
                workflowVersion = version.Version,
                workflowVersionId = version.Id,
                subjectType = PipelineWorkflowHelpers.FormatSubjectType(subjectType),
                subjectId = request.SubjectId,
                patientId = pipeline.PatientId,
                encounterId = pipeline.EncounterId,
                currentNodeId = pipeline.CurrentNodeId,
            });

        auditWriter.Append(
            AuditEntityTypes.Pipeline,
            pipeline.Id,
            "pipeline.started",
            actorId,
            now,
            new
            {
                workflowCode = request.WorkflowCode,
                workflowVersion = version.Version,
                workflowVersionId = version.Id,
                subjectType = PipelineWorkflowHelpers.FormatSubjectType(subjectType),
                subjectId = request.SubjectId,
                patientId = pipeline.PatientId,
                encounterId = pipeline.EncounterId,
            });

        pipelines.Add(pipeline);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineMappers.ToDto(pipeline);
    }

    /// <inheritdoc />
    public async Task<PipelineDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);
        Pipeline pipeline = await RequirePipelineAsync(
                id,
                track: false,
                cancellationToken)
            .ConfigureAwait(false);
        return PipelineMappers.ToDto(pipeline);
    }

    /// <inheritdoc />
    public async Task<PipelineListResponse> ListAsync(
        PipelineListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);

        PipelineSubjectType? subjectType = PipelineWorkflowHelpers
            .ParseSubjectTypeOrNull(request.SubjectType);
        PipelineStatus? status = PipelineWorkflowHelpers.ParseStatusOrNull(request.Status);
        IReadOnlyList<Pipeline> items = await pipelines
            .ListAsync(
                hospitalContext.HospitalId,
                new PipelineListCriteria(
                    subjectType,
                    request.SubjectId,
                    status,
                    request.PatientId,
                    request.EncounterId),
                cancellationToken)
            .ConfigureAwait(false);
        return new PipelineListResponse(
            [.. items.Select(PipelineMappers.ToDto)]);
    }

    /// <inheritdoc />
    public async Task<PatientJourneyResponse> GetPatientJourneyAsync(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);

        // A soft-deleted patient stays queryable so historical journeys
        // keep rendering; only an unknown or cross-tenant id is a 404.
        _ = await patients
            .FindByIdAsync(
                hospitalContext.HospitalId,
                patientId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Patient '{patientId}' was not found.");

        IReadOnlyList<Pipeline> items = await pipelines
            .ListForJourneyAsync(
                hospitalContext.HospitalId,
                new PipelineListCriteria(
                    SubjectType: null,
                    SubjectId: null,
                    Status: null,
                    PatientId: patientId,
                    EncounterId: null),
                cancellationToken)
            .ConfigureAwait(false);
        return new PatientJourneyResponse(
            patientId,
            [.. items.Select(PipelineMappers.ToJourneyDto)]);
    }

    /// <inheritdoc />
    public async Task<EncounterJourneyResponse> GetEncounterJourneyAsync(
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);

        _ = await encounters
            .FindByIdAsync(
                hospitalContext.HospitalId,
                encounterId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Encounter '{encounterId}' was not found.");

        IReadOnlyList<Pipeline> items = await pipelines
            .ListForJourneyAsync(
                hospitalContext.HospitalId,
                new PipelineListCriteria(
                    SubjectType: null,
                    SubjectId: null,
                    Status: null,
                    PatientId: null,
                    EncounterId: encounterId),
                cancellationToken)
            .ConfigureAwait(false);
        return new EncounterJourneyResponse(
            encounterId,
            [.. items.Select(PipelineMappers.ToJourneyDto)]);
    }

    /// <inheritdoc />
    public async Task<PipelineHistoryResponse> ListHistoryAsync(
        Guid pipelineId,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);

        // Resolve the pipeline first so an unknown or cross-tenant id is a
        // 404 rather than an empty history.
        _ = await RequirePipelineAsync(
                pipelineId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<PipelineHistory> history = await pipelines
            .ListHistoryAsync(
                hospitalContext.HospitalId,
                pipelineId,
                cancellationToken)
            .ConfigureAwait(false);
        return new PipelineHistoryResponse(
            pipelineId,
            [.. history.Select(PipelineMappers.ToHistoryDto)]);
    }

    /// <inheritdoc />
    public async Task<PipelineDto> AdvanceAsync(
        Guid id,
        AdvancePipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesWrite, cancellationToken)
            .ConfigureAwait(false);

        Pipeline pipeline = await RequirePipelineAsync(
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false);
        PipelineWorkflowHelpers.EnsureConcurrency(pipeline.RowVersion, request.RowVersion);
        if (pipeline.Status != PipelineStatus.Running)
        {
            throw new InvalidStateException(
                "Cannot advance a pipeline in status '"
                + PipelineWorkflowHelpers.FormatStatus(pipeline.Status)
                + "'.");
        }

        WorkflowGraph graph = WorkflowGraphReader.Read(
            pipeline.WorkflowVersion.WorkflowSchemaJson);
        WorkflowEdge edge = ResolveOutgoingEdge(
            graph,
            pipeline.CurrentNodeId,
            request.InputValues);
        WorkflowNode next = graph.RequireNode(edge.To);

        DateTimeOffset now = timeProvider.GetUtcNow();
        pipeline.RowVersion = request.RowVersion + 1;
        pipeline.UpdatedAt = now;
        if (string.Equals(next.Type, WorkflowGraph.EndType, StringComparison.Ordinal))
        {
            await CancelOpenTasksAsync(pipeline, actorId, now, cancellationToken)
                .ConfigureAwait(false);
            pipeline.Status = PipelineStatus.Completed;
            pipeline.EndedAt = now;
            pipeline.CurrentNodeId = next.Id;
            PipelineMappers.AppendHistory(
                pipeline,
                actorId,
                now,
                "pipeline.completed",
                new
                {
                    fromNodeId = edge.From,
                    toNodeId = next.Id,
                    edgeLabel = edge.Label,
                });
            auditWriter.Append(
                AuditEntityTypes.Pipeline,
                pipeline.Id,
                "pipeline.completed",
                actorId,
                now,
                new
                {
                    fromNodeId = edge.From,
                    toNodeId = next.Id,
                    currentNodeId = next.Id,
                });
        }
        else
        {
            pipeline.CurrentNodeId = next.Id;
            if (string.Equals(next.Type, WorkflowGraph.TaskType, StringComparison.Ordinal))
            {
                await CreateTaskForNodeAsync(pipeline, next, actorId, now, cancellationToken)
                    .ConfigureAwait(false);
            }

            PipelineMappers.AppendHistory(
                pipeline,
                actorId,
                now,
                "pipeline.advanced",
                new
                {
                    fromNodeId = edge.From,
                    toNodeId = next.Id,
                    edgeLabel = edge.Label,
                });
            auditWriter.Append(
                AuditEntityTypes.Pipeline,
                pipeline.Id,
                "pipeline.advanced",
                actorId,
                now,
                new
                {
                    fromNodeId = edge.From,
                    toNodeId = next.Id,
                    currentNodeId = next.Id,
                });
        }

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineMappers.ToDto(pipeline);
    }

    /// <inheritdoc />
    public async Task<PipelineDto> CompleteAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        return await FireLifecycleAsync(
                id,
                request,
                actorId,
                PipelineLifecycle.Trigger.Complete,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PipelineDto> CancelAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        return await FireLifecycleAsync(
                id,
                request,
                actorId,
                PipelineLifecycle.Trigger.Cancel,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PipelineDto> EnterInErrorAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        return await FireLifecycleAsync(
                id,
                request,
                actorId,
                PipelineLifecycle.Trigger.EnterInError,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PipelineDto> FireLifecycleAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        PipelineLifecycle.Trigger trigger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesWrite, cancellationToken)
            .ConfigureAwait(false);

        Pipeline pipeline = await RequirePipelineAsync(
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false);
        PipelineWorkflowHelpers.EnsureConcurrency(pipeline.RowVersion, request.RowVersion);
        PipelineLifecycle.Fire(pipeline, trigger);

        string reason = PipelineWorkflowHelpers.EnsureReasonLength(request.Reason);
        string action = trigger switch
        {
            PipelineLifecycle.Trigger.Complete => "pipeline.completed",
            PipelineLifecycle.Trigger.Cancel => "pipeline.canceled",
            PipelineLifecycle.Trigger.EnterInError => "pipeline.entered-in-error",
            _ => throw new InvalidOperationException(
                $"Unknown pipeline lifecycle trigger '{trigger}'."),
        };
        DateTimeOffset now = timeProvider.GetUtcNow();
        pipeline.EndedAt = now;
        pipeline.UpdatedAt = now;
        pipeline.RowVersion = request.RowVersion + 1;

        await CancelOpenTasksAsync(pipeline, actorId, now, cancellationToken)
            .ConfigureAwait(false);

        PipelineMappers.AppendHistory(
            pipeline,
            actorId,
            now,
            action,
            new
            {
                reason,
                currentNodeId = pipeline.CurrentNodeId,
            });
        auditWriter.Append(
            AuditEntityTypes.Pipeline,
            pipeline.Id,
            action,
            actorId,
            now,
            new
            {
                reason,
                currentNodeId = pipeline.CurrentNodeId,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineMappers.ToDto(pipeline);
    }

    private async Task CreateTaskForNodeAsync(
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
            });
    }

    private async Task CancelOpenTasksAsync(
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
                });
        }
    }

    private async Task<Pipeline> RequirePipelineAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        Pipeline? pipeline = await pipelines
            .FindByIdAsync(hospitalContext.HospitalId, id, track, cancellationToken)
            .ConfigureAwait(false);
        return pipeline ?? throw new NotFoundException(
            $"Workflow pipeline '{id}' was not found.");
    }

    private async Task<PipelineSubjectBinding> RequireActiveEncounterAsync(
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        Encounter encounter = await encounters
            .FindByIdAsync(
                hospitalContext.HospitalId,
                encounterId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Encounter '{encounterId}' was not found.");

        if (encounter.Status != EncounterStatus.Open)
        {
            throw new InvalidStateException(
                $"Encounter '{encounterId}' is "
                + EncounterWorkflowHelpers.FormatStatus(encounter.Status)
                + "; pipelines can only be started for open encounters.");
        }

        return new PipelineSubjectBinding(encounter.PatientId, encounter.Id);
    }

    private async Task<PipelineSubjectBinding> RequireActivePatientAsync(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        Patient patient = await patients
            .FindByIdAsync(
                hospitalContext.HospitalId,
                patientId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Patient '{patientId}' was not found.");

        if (patient.DeletedAt is not null)
        {
            throw new InvalidStateException(
                $"Patient '{patientId}' is deleted and cannot start a "
                + "new pipeline.");
        }

        return new PipelineSubjectBinding(patient.Id, EncounterId: null);
    }

    private async Task<WorkflowVersion> RequirePublishedVersionAsync(
        string workflowCode,
        string? version,
        CancellationToken cancellationToken)
    {
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(
                workflows,
                workflowCode,
                track: true,
                hospitalContext.HospitalId,
                cancellationToken)
            .ConfigureAwait(false);

        WorkflowVersion published;
        if (string.IsNullOrWhiteSpace(version))
        {
            published = definition.Versions
                    .Where(item => item.Status == WorkflowVersionStatus.Published
                        && item.Version != null)
                    .OrderBy(item => item.Version!, SemverRules.StringComparer)
                    .LastOrDefault()
                ?? throw new NotFoundException(
                    $"Workflow '{workflowCode}' has no published version.");
        }
        else
        {
            SemverRules.EnsureValid(version);
            published = definition.Versions.SingleOrDefault(
                    item => string.Equals(item.Version, version, StringComparison.Ordinal)
                        && item.Status == WorkflowVersionStatus.Published)
                ?? throw new NotFoundException(
                    $"Published workflow '{workflowCode}' version '{version}' was not found.");
        }

        return published;
    }

    private static WorkflowEdge ResolveOutgoingEdge(
        WorkflowGraph graph,
        string currentNodeId,
        IReadOnlyDictionary<string, JsonElement>? inputValues)
    {
        IReadOnlyList<WorkflowEdge> outgoing = graph.Outgoing(currentNodeId);
        if (outgoing.Count == 0)
        {
            throw new InvalidStateException(
                $"Workflow node '{currentNodeId}' has no outgoing transition.");
        }

        if (string.Equals(
                graph.RequireNode(currentNodeId).Type,
                WorkflowGraph.DecisionType,
                StringComparison.Ordinal))
        {
            foreach (WorkflowEdge edge in outgoing)
            {
                if (edge.Condition is not null
                    && WorkflowConditionEvaluator.Evaluate(
                        edge.Condition.Value,
                        inputValues))
                {
                    return edge;
                }
            }

            WorkflowEdge? fallback = outgoing.SingleOrDefault(
                edge => edge.Condition is null);
            if (fallback is not null)
            {
                return fallback;
            }

            throw new InvalidStateException(
                "No workflow transition matched for decision node '"
                + currentNodeId
                + "' with the supplied inputs.");
        }

        return outgoing[0];
    }

    /// <summary>Resolved patient/encounter binding for a pipeline subject.</summary>
    private sealed record PipelineSubjectBinding(
        Guid PatientId,
        Guid? EncounterId);
}
