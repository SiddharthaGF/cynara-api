using System.Text.Json;

using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Workflows.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Default implementation of <see cref="IPipelineService"/>. Starting pins
/// the exact published workflow version and validates the subject; every
/// transition re-reads the immutable schema graph, appends immutable
/// progression history, and commits its audit event in one unit of work.
/// </summary>
public sealed class PipelineService(
    IPipelineRepository pipelines,
    IWorkflowRepository workflows,
    PipelineSubjectResolver subjectResolver,
    PipelineTaskCoordinator taskCoordinator,
    TransactionalDeps transactional,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider) : IPipelineService
{
    /// <inheritdoc />
    public async Task<PipelineDto> StartAsync(
        StartPipelineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await transactional.CapabilityGuard.RequireAsync(
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

        PipelineSubjectBinding subject = await subjectResolver.ResolveAsync(
                subjectType,
                request.SubjectId,
                cancellationToken)
            .ConfigureAwait(false);

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

        transactional.AuditWriter.Append(
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
            },
            patientId: pipeline.PatientId,
            encounterId: pipeline.EncounterId,
            workflowDefinitionId: version.WorkflowDefinitionId);

        pipelines.Add(pipeline);
        _ = await transactional.UnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineMappers.ToDto(pipeline);
    }

    /// <inheritdoc />
    public async Task<PipelineDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await transactional.CapabilityGuard.RequireAsync(
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
        await transactional.CapabilityGuard.RequireAsync(
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
        await transactional.CapabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);

        await subjectResolver.EnsureSubjectExistsAsync(
                PipelineSubjectType.Patient,
                patientId,
                cancellationToken)
            .ConfigureAwait(false);

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
        await transactional.CapabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);

        await subjectResolver.EnsureSubjectExistsAsync(
                PipelineSubjectType.Encounter,
                encounterId,
                cancellationToken)
            .ConfigureAwait(false);

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
        await transactional.CapabilityGuard.RequireAsync(
            CapabilityCodes.PipelinesRead, cancellationToken)
            .ConfigureAwait(false);

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
        await transactional.CapabilityGuard.RequireAsync(
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
            await taskCoordinator.CancelOpenAsync(
                    pipeline,
                    actorId,
                    now,
                    cancellationToken)
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
            transactional.AuditWriter.Append(
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
                },
                patientId: pipeline.PatientId,
                encounterId: pipeline.EncounterId,
                workflowDefinitionId: pipeline.WorkflowVersion.WorkflowDefinitionId);
        }
        else
        {
            pipeline.CurrentNodeId = next.Id;
            if (string.Equals(next.Type, WorkflowGraph.TaskType, StringComparison.Ordinal))
            {
                await taskCoordinator.CreateForNodeAsync(
                        pipeline,
                        next,
                        actorId,
                        now,
                        cancellationToken)
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
            transactional.AuditWriter.Append(
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
                },
                patientId: pipeline.PatientId,
                encounterId: pipeline.EncounterId,
                workflowDefinitionId: pipeline.WorkflowVersion.WorkflowDefinitionId);
        }

        _ = await transactional.UnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
                TerminalLifecycle.Trigger.Complete,
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
                TerminalLifecycle.Trigger.Cancel,
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
                TerminalLifecycle.Trigger.EnterInError,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PipelineDto> FireLifecycleAsync(
        Guid id,
        TransitionPipelineRequest request,
        string? actorId,
        TerminalLifecycle.Trigger trigger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await transactional.CapabilityGuard.RequireAsync(
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
            TerminalLifecycle.Trigger.Complete => "pipeline.completed",
            TerminalLifecycle.Trigger.Cancel => "pipeline.canceled",
            TerminalLifecycle.Trigger.EnterInError => "pipeline.entered-in-error",
            _ => throw new InvalidOperationException(
                $"Unknown pipeline lifecycle trigger '{trigger}'."),
        };
        DateTimeOffset now = timeProvider.GetUtcNow();
        pipeline.EndedAt = now;
        pipeline.UpdatedAt = now;
        pipeline.RowVersion = request.RowVersion + 1;

        await taskCoordinator.CancelOpenAsync(
                pipeline,
                actorId,
                now,
                cancellationToken)
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
        transactional.AuditWriter.Append(
            AuditEntityTypes.Pipeline,
            pipeline.Id,
            action,
            actorId,
            now,
            new
            {
                reason,
                currentNodeId = pipeline.CurrentNodeId,
            },
            patientId: pipeline.PatientId,
            encounterId: pipeline.EncounterId,
            workflowDefinitionId: pipeline.WorkflowVersion.WorkflowDefinitionId);

        _ = await transactional.UnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineMappers.ToDto(pipeline);
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

        if (!string.IsNullOrWhiteSpace(version))
        {
            SemverRules.EnsureValid(version);
            return definition.Versions.SingleOrDefault(
                    item => string.Equals(item.Version, version, StringComparison.Ordinal)
                        && item.Status == WorkflowVersionStatus.Published)
                ?? throw new NotFoundException(
                    $"Published workflow '{workflowCode}' version '{version}' was not found.");
        }

        return definition.Versions
            .Where(item => item.Status == WorkflowVersionStatus.Published
                && item.Version != null)
            .OrderBy(item => item.Version!, SemverRules.StringComparer)
            .LastOrDefault()
            ?? throw new NotFoundException(
                $"Workflow '{workflowCode}' has no published version.");
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
            WorkflowEdge? conditional = outgoing.FirstOrDefault(
                edge => edge.Condition is not null
                    && WorkflowConditionEvaluator.Evaluate(
                        edge.Condition.Value,
                        inputValues));
            if (conditional is not null)
            {
                return conditional;
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
}
