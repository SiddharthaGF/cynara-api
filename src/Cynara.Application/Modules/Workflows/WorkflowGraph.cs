using System.Text.Json;

namespace Cynara.Application.Modules.Workflows;

/// <summary>Parsed node of a workflow graph.</summary>
/// <param name="Id">Stable node id within the workflow.</param>
/// <param name="Type">One of <c>start</c>, <c>end</c>, <c>task</c>, <c>decision</c>.</param>
/// <param name="Name">Human-readable display name, when present.</param>
/// <param name="Description">Optional free-text node description.</param>
/// <param name="Assignee">Optional task assignee, when present on a task node.</param>
/// <param name="FormCode">Optional referenced form code, when present on a task node.</param>
/// <param name="FormVersion">Optional pinned form version, when present on a task node.</param>
/// <param name="DueDays">Optional calendar days from task creation to the due date.</param>
internal sealed record WorkflowNode(
    string Id,
    string Type,
    string? Name,
    string? Description = null,
    WorkflowAssignee? Assignee = null,
    string? FormCode = null,
    string? FormVersion = null,
    int? DueDays = null);

/// <summary>Assignee of a workflow task node.</summary>
/// <param name="Actor">Specific actor or automated system.</param>
/// <param name="Role">Functional role.</param>
/// <param name="Discipline">Clinical discipline.</param>
internal sealed record WorkflowAssignee(
    string? Actor = null,
    string? Role = null,
    string? Discipline = null);

/// <summary>Parsed directed edge of a workflow graph.</summary>
/// <param name="From">Source node id.</param>
/// <param name="To">Target node id.</param>
/// <param name="Label">Human-readable transition label, when present.</param>
/// <param name="Condition">
/// Transition guard expression; <see langword="null"/> for the unconditional
/// default edge of a decision node.
/// </param>
internal sealed record WorkflowEdge(
    string From,
    string To,
    string? Label,
    JsonElement? Condition);

/// <summary>
/// In-memory projection of a published workflow graph used by the pipeline
/// runtime. Built from the immutable published schema so the runtime always
/// walks the exact version the pipeline started on.
/// </summary>
internal sealed class WorkflowGraph
{
    public const string StartType = "start";
    public const string EndType = "end";
    public const string TaskType = "task";
    public const string DecisionType = "decision";

    private readonly Dictionary<string, WorkflowNode> nodes;
    private readonly Dictionary<string, IReadOnlyList<WorkflowEdge>> outgoing;

    public WorkflowGraph(
        IReadOnlyList<WorkflowNode> nodes,
        IReadOnlyList<WorkflowEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        this.nodes = nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);
        outgoing = edges
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WorkflowEdge>)[.. group],
                StringComparer.Ordinal);
        Nodes = [.. nodes];
        Edges = [.. edges];
    }

    /// <summary>All nodes of the graph in document order.</summary>
    public IReadOnlyList<WorkflowNode> Nodes { get; }

    /// <summary>All edges of the graph in document order.</summary>
    public IReadOnlyList<WorkflowEdge> Edges { get; }

    /// <summary>Returns the single start node of the graph.</summary>
    public WorkflowNode StartNode => nodes.Values.Single(
        node => string.Equals(
            node.Type,
            StartType,
            StringComparison.Ordinal));

    /// <summary>Returns the node with the supplied id.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the node is not part of the published graph.
    /// </exception>
    public WorkflowNode RequireNode(string id)
    {
        return nodes.TryGetValue(id, out WorkflowNode? node)
            ? node
            : throw new InvalidOperationException(
                $"Workflow node '{id}' is not part of the pinned workflow graph.");
    }

    /// <summary>Returns the outgoing edges of the supplied node in document order.</summary>
    public IReadOnlyList<WorkflowEdge> Outgoing(string nodeId)
    {
        return outgoing.TryGetValue(nodeId, out IReadOnlyList<WorkflowEdge>? edges)
            ? edges
            : [];
    }
}
