using System.Text.Json;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Parses a published workflow schema document into a <see cref="WorkflowGraph"/>
/// for the pipeline runtime. Input schemas are validated before publish, so
/// structural violations here indicate corrupted data and throw
/// <see cref="ValidationException"/> defensively.
/// </summary>
internal static class WorkflowGraphReader
{
    public static WorkflowGraph Read(string workflowSchemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowSchemaJson);
        using var document = JsonDocument.Parse(workflowSchemaJson);
        JsonElement root = document.RootElement;

        List<WorkflowNode> nodes = ReadNodes(root);
        List<WorkflowEdge> edges = ReadEdges(root);
        return new WorkflowGraph(nodes, edges);
    }

    private static List<WorkflowNode> ReadNodes(JsonElement root)
    {
        var nodes = new List<WorkflowNode>();
        if (!root.TryGetProperty("nodes", out JsonElement nodesElement)
            || nodesElement.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException(
                "Published workflow schema is missing the 'nodes' array.");
        }

        foreach (JsonElement node in nodesElement.EnumerateArray())
        {
            string id = ReadString(node, "id");
            string type = ReadString(node, "type");
            nodes.Add(new WorkflowNode(
                id,
                type,
                ReadOptionalString(node, "name"),
                ReadOptionalString(node, "description"),
                ReadAssignee(node),
                ReadOptionalString(node, "formCode"),
                ReadOptionalString(node, "formVersion"),
                ReadOptionalInt(node, "dueDays")));
        }

        return nodes;
    }

    private static WorkflowAssignee? ReadAssignee(JsonElement node)
    {
        if (!node.TryGetProperty("assignee", out JsonElement assignee)
            || assignee.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new WorkflowAssignee(
            ReadOptionalString(assignee, "actor"),
            ReadOptionalString(assignee, "role"),
            ReadOptionalString(assignee, "discipline"));
    }

    private static int? ReadOptionalInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number)
            ? number
            : null;
    }

    private static List<WorkflowEdge> ReadEdges(JsonElement root)
    {
        var edges = new List<WorkflowEdge>();
        if (!root.TryGetProperty("edges", out JsonElement edgesElement)
            || edgesElement.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException(
                "Published workflow schema is missing the 'edges' array.");
        }

        foreach (JsonElement edge in edgesElement.EnumerateArray())
        {
            string from = ReadString(edge, "from");
            string to = ReadString(edge, "to");
            JsonElement? condition = edge.TryGetProperty(
                "condition",
                out JsonElement conditionElement)
                ? conditionElement.Clone()
                : null;
            edges.Add(new WorkflowEdge(
                from,
                to,
                ReadOptionalString(edge, "label"),
                condition));
        }

        return edges;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    private static string? ReadOptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
