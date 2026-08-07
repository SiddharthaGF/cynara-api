using System.Text.Json;

using Cynara.Application.Common;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Projects pipeline aggregates and history rows to their public DTO shapes.
/// </summary>
internal static class PipelineMappers
{
    public static PipelineDto ToDto(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        WorkflowVersion version = pipeline.WorkflowVersion
            ?? throw new InvalidOperationException(
                "Pipeline is missing its pinned workflow version.");
        return new PipelineDto(
            pipeline.Id,
            version.WorkflowDefinition.Code,
            version.Version ?? string.Empty,
            version.Id,
            version.PublishedSchemaVersion ?? string.Empty,
            PipelineWorkflowHelpers.FormatSubjectType(pipeline.SubjectType),
            pipeline.SubjectId,
            pipeline.PatientId,
            pipeline.EncounterId,
            PipelineWorkflowHelpers.FormatStatus(pipeline.Status),
            pipeline.CurrentNodeId,
            pipeline.StartedAt,
            pipeline.EndedAt,
            pipeline.RowVersion,
            pipeline.CreatedAt,
            pipeline.UpdatedAt);
    }

    public static PipelineHistoryDto ToHistoryDto(PipelineHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return new PipelineHistoryDto(
            history.Id,
            history.PipelineId,
            history.Sequence,
            history.Action,
            history.ActorId,
            history.OccurredAt,
            history.MetadataJson);
    }

    /// <summary>
    /// Projects a pipeline (with its pinned workflow version and history
    /// loaded) into a journey rendered from the exact published graph.
    /// </summary>
    public static JourneyDto ToJourneyDto(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        WorkflowVersion version = pipeline.WorkflowVersion
            ?? throw new InvalidOperationException(
                "Pipeline is missing its pinned workflow version.");
        WorkflowGraph graph = WorkflowGraphReader.Read(version.WorkflowSchemaJson);
        return new JourneyDto(
            pipeline.Id,
            version.WorkflowDefinition.Code,
            version.Version ?? string.Empty,
            version.Id,
            version.PublishedSchemaVersion ?? string.Empty,
            PipelineWorkflowHelpers.FormatSubjectType(pipeline.SubjectType),
            pipeline.SubjectId,
            pipeline.PatientId,
            pipeline.EncounterId,
            PipelineWorkflowHelpers.FormatStatus(pipeline.Status),
            pipeline.CurrentNodeId,
            pipeline.StartedAt,
            pipeline.EndedAt,
            ToGraphDto(graph),
            [.. pipeline.History
                .OrderBy(item => item.Sequence)
                .Select(ToHistoryDto)]);
    }

    /// <summary>Appends a new history event onto the pipeline aggregate.</summary>
    public static void AppendHistory(
        Pipeline pipeline,
        string? actorId,
        DateTimeOffset occurredAt,
        string action,
        object metadata)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _ = pipeline.History.Add(new PipelineHistory
        {
            Id = Guid.NewGuid(),
            HospitalId = pipeline.HospitalId,
            PipelineId = pipeline.Id,
            Sequence = pipeline.History.Count + 1,
            Action = action,
            ActorId = actorId,
            OccurredAt = occurredAt,
            MetadataJson = JsonSerializer.Serialize(
                metadata,
                CanonicalJsonOptions.Instance),
        });
    }

    private static WorkflowGraphDto ToGraphDto(WorkflowGraph graph)
    {
        return new WorkflowGraphDto(
            [.. graph.Nodes.Select(
                node => new WorkflowNodeDto(node.Id, node.Type, node.Name))],
            [.. graph.Edges.Select(
                edge => new WorkflowEdgeDto(edge.From, edge.To, edge.Label))]);
    }
}
