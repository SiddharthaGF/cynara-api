using System.Globalization;
using System.Text.Json;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Semantic rules WORK-001..WORK-016 from the Stage 3 semantic-rules contract.
/// Structural JSON Schema validation runs first (see
/// <c>Cynara.Infrastructure.Schemas.JsonSchemaValidator</c>); this class
/// enforces the graph rules a schema cannot express: unique node ids, entry/
/// exit points, per-node output arity, transition conditions, connectivity,
/// acyclicity, and form-version pinning for published workflows.
/// </summary>
public static class WorkflowSemanticValidator
{
    public static void Validate(string workflowSchemaJson, bool published)
    {
        ArgumentNullException.ThrowIfNull(workflowSchemaJson);
        using var document = JsonDocument.Parse(workflowSchemaJson);
        var collector = new ErrorCollector(document.RootElement, published);
        List<string> errors = collector.Run();
        if (errors.Count == 0)
        {
            return;
        }

        throw new ValidationException(
            $"Invalid workflow schema: {string.Join("; ", errors)}");
    }

    private sealed class ErrorCollector
    {
        private const string StartType = "start";
        private const string EndType = "end";
        private const string TaskType = "task";
        private const string DecisionType = "decision";

        private readonly JsonElement root;
        private readonly bool published;
        private readonly HashSet<string> inputs = new(StringComparer.Ordinal);
        private readonly List<NodeInfo> nodes = [];
        private readonly List<EdgeInfo> edges = [];
        private readonly List<string> errors = [];

        public ErrorCollector(JsonElement root, bool published)
        {
            this.root = root;
            this.published = published;
        }

        public List<string> Run()
        {
            ReadInputs();
            ReadNodes();
            ReadEdges();
            ValidateEntryAndExit();
            ValidatePerNodeRules();
            ValidateReachability();
            ValidateAcyclicity();
            return errors;
        }

        private void ReadInputs()
        {
            if (!root.TryGetProperty("inputs", out JsonElement inputsElement)
                || inputsElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement input in inputsElement.EnumerateArray())
            {
                if (input.ValueKind == JsonValueKind.String)
                {
                    _ = inputs.Add(input.GetString()!);
                }
            }
        }

        private void ReadNodes()
        {
            if (!root.TryGetProperty("nodes", out JsonElement nodesElement)
                || nodesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement node in nodesElement.EnumerateArray())
            {
                string id = ReadString(node, "id");
                string type = ReadString(node, "type");
                if (!seenIds.Add(id))
                {
                    Add(
                        "DUPLICATE_NODE_ID",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"/nodes/{index}/id"),
                        $"Node id '{id}' is already used.");
                }
                else
                {
                    int? dueDays = node.TryGetProperty("dueDays", out JsonElement dueDaysElement)
                            && dueDaysElement.ValueKind == JsonValueKind.Number
                            && dueDaysElement.TryGetInt32(out int dueDaysValue)
                        ? dueDaysValue
                        : null;
                    nodes.Add(new NodeInfo(
                        id,
                        type,
                        HasFormCode: node.TryGetProperty("formCode", out JsonElement _),
                        HasFormVersion: node.TryGetProperty("formVersion", out JsonElement _),
                        DueDays: dueDays));
                }

                index++;
            }
        }

        private void ReadEdges()
        {
            if (!root.TryGetProperty("edges", out JsonElement edgesElement)
                || edgesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var knownIds = nodes.Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement edge in edgesElement.EnumerateArray())
            {
                string from = ReadString(edge, "from");
                string to = ReadString(edge, "to");
                bool hasCondition = edge.TryGetProperty("condition", out JsonElement _);
                edges.Add(new EdgeInfo(from, to, hasCondition));

                if (!knownIds.Contains(from))
                {
                    Add(
                        "EDGE_UNKNOWN_NODE",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"/edges/{index}/from"),
                        $"Edge source '{from}' references an unknown node.");
                }

                if (!knownIds.Contains(to))
                {
                    Add(
                        "EDGE_UNKNOWN_NODE",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"/edges/{index}/to"),
                        $"Edge target '{to}' references an unknown node.");
                }

                if (hasCondition)
                {
                    ValidateConditionRefs(edge.GetProperty("condition"), index);
                }

                index++;
            }
        }

        private void ValidateConditionRefs(JsonElement condition, int edgeIndex)
        {
            var unknown = new List<string>();
            CollectUnknownRefs(condition, unknown);
            if (unknown.Count == 0)
            {
                return;
            }

            string detail = "Condition references undeclared input"
                + $"{(unknown.Count == 1 ? string.Empty : "s")} "
                + $"'{string.Join("', '", unknown)}'.";
            Add(
                "CONDITION_UNKNOWN_REF",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"/edges/{edgeIndex}/condition"),
                detail);
        }

        private void CollectUnknownRefs(JsonElement expression, List<string> unknown)
        {
            if (expression.ValueKind == JsonValueKind.Object
                && expression.TryGetProperty("ref", out JsonElement refElement)
                && refElement.ValueKind == JsonValueKind.String)
            {
                string reference = refElement.GetString()!;
                if (!inputs.Contains(reference))
                {
                    unknown.Add(reference);
                }

                return;
            }

            if (expression.ValueKind == JsonValueKind.Object
                && expression.TryGetProperty("args", out JsonElement args)
                && args.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement argument in args.EnumerateArray())
                {
                    CollectUnknownRefs(argument, unknown);
                }
            }
        }

        private void ValidateEntryAndExit()
        {
            bool hasStart = nodes.Exists(item => string.Equals(
                item.Type,
                StartType,
                StringComparison.Ordinal));
            if (!hasStart)
            {
                Add(
                    "ENTRY_REQUIRED",
                    "/nodes",
                    "A workflow must include exactly one 'start' node.");
            }

            bool hasEnd = nodes.Exists(item => string.Equals(
                item.Type,
                EndType,
                StringComparison.Ordinal));
            if (!hasEnd)
            {
                Add(
                    "EXIT_REQUIRED",
                    "/nodes",
                    "A workflow must include at least one 'end' node.");
            }
        }

        private void ValidatePerNodeRules()
        {
            var outgoingByNode = new Dictionary<string, List<EdgeInfo>>(
                StringComparer.Ordinal);
            var incomingByNode = new Dictionary<string, List<EdgeInfo>>(
                StringComparer.Ordinal);
            foreach (EdgeInfo edge in edges)
            {
                AddEdge(outgoingByNode, edge.From, edge);
                AddEdge(incomingByNode, edge.To, edge);
            }

            bool startSeen = false;
            for (int index = 0; index < nodes.Count; index++)
            {
                NodeInfo node = nodes[index];
                IReadOnlyList<EdgeInfo> outgoing = outgoingByNode.TryGetValue(
                        node.Id,
                        out List<EdgeInfo>? list)
                    ? list
                    : [];
                IReadOnlyList<EdgeInfo> incoming = incomingByNode.TryGetValue(
                        node.Id,
                        out List<EdgeInfo>? incomingList)
                    ? incomingList
                    : [];
                string nodePath = NodePath(index);

                if (string.Equals(node.Type, StartType, StringComparison.Ordinal))
                {
                    ValidateStartNode(node, outgoing, incoming, nodePath, startSeen);
                    startSeen = true;
                }
                else if (string.Equals(node.Type, EndType, StringComparison.Ordinal))
                {
                    ValidateEndNode(node, outgoing, nodePath);
                }
                else if (string.Equals(node.Type, TaskType, StringComparison.Ordinal))
                {
                    ValidateTaskNode(node, outgoing, nodePath);
                }
                else if (string.Equals(node.Type, DecisionType, StringComparison.Ordinal))
                {
                    ValidateDecisionNode(node, outgoing, nodePath);
                }
            }
        }

        private void ValidateStartNode(
            NodeInfo node,
            IReadOnlyList<EdgeInfo> outgoing,
            IReadOnlyList<EdgeInfo> incoming,
            string nodePath,
            bool startSeen)
        {
            if (startSeen)
            {
                Add(
                    "ENTRY_UNIQUE",
                    nodePath,
                    $"Additional start node '{node.Id}' violates the unique entry rule.");
            }

            if (incoming.Count > 0)
            {
                Add(
                    "ENTRY_INCOMING_EDGE",
                    nodePath,
                    $"Start node '{node.Id}' must not have incoming edges.");
            }

            if (outgoing.Count != 1)
            {
                Add(
                    "ENTRY_SINGLE_OUTPUT",
                    nodePath,
                    $"Start node '{node.Id}' must have exactly one outgoing edge.");
            }
        }

        private void ValidateEndNode(
            NodeInfo node,
            IReadOnlyList<EdgeInfo> outgoing,
            string nodePath)
        {
            if (outgoing.Count > 0)
            {
                Add(
                    "EXIT_OUTGOING_EDGE",
                    nodePath,
                    $"End node '{node.Id}' must not have outgoing edges.");
            }
        }

        private void ValidateTaskNode(
            NodeInfo node,
            IReadOnlyList<EdgeInfo> outgoing,
            string nodePath)
        {
            if (outgoing.Count != 1)
            {
                Add(
                    "TASK_SINGLE_OUTPUT",
                    nodePath,
                    $"Task node '{node.Id}' must have exactly one outgoing edge.");
            }
            else if (outgoing[0].HasCondition)
            {
                Add(
                    "TASK_UNCONDITIONAL_OUTPUT",
                    nodePath,
                    $"Task node '{node.Id}' outgoing edge must not carry a condition.");
            }

            if (published && node.HasFormCode && !node.HasFormVersion)
            {
                Add(
                    "FORM_VERSION_REQUIRED",
                    nodePath + "/formVersion",
                    $"Task node '{node.Id}' must pin formVersion when formCode is set.");
            }

            if (node.DueDays is int dueDays && dueDays < 1)
            {
                Add(
                    "TASK_DUE_DAYS_INVALID",
                    nodePath + "/dueDays",
                    $"Task node '{node.Id}' dueDays must be a positive integer.");
            }
        }

        private void ValidateDecisionNode(
            NodeInfo node,
            IReadOnlyList<EdgeInfo> outgoing,
            string nodePath)
        {
            if (outgoing.Count < 2)
            {
                Add(
                    "DECISION_OUTPUTS",
                    nodePath,
                    $"Decision node '{node.Id}' must have at least two outgoing edges.");
            }

            int unconditionalCount = 0;
            foreach (EdgeInfo edge in outgoing)
            {
                if (!edge.HasCondition)
                {
                    unconditionalCount++;
                }
            }

            if (unconditionalCount > 1)
            {
                Add(
                    "DECISION_DEFAULT_EDGE",
                    nodePath,
                    $"Decision node '{node.Id}' must have at most one unconditional default edge.");
            }
        }

        private void ValidateReachability()
        {
            var adjacency = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            foreach (EdgeInfo edge in edges)
            {
                AddTarget(adjacency, edge.From, edge.To);
            }

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            NodeInfo? start = nodes.SingleOrDefault(item => string.Equals(
                item.Type,
                StartType,
                StringComparison.Ordinal));
            if (start is not null)
            {
                queue.Enqueue(start.Id);
                _ = reachable.Add(start.Id);
            }

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out List<string>? targets))
                {
                    continue;
                }

                foreach (string target in targets)
                {
                    if (reachable.Add(target))
                    {
                        queue.Enqueue(target);
                    }
                }
            }

            for (int index = 0; index < nodes.Count; index++)
            {
                if (reachable.Contains(nodes[index].Id))
                {
                    continue;
                }

                Add(
                    "UNREACHABLE_NODE",
                    NodePath(index),
                    $"Node '{nodes[index].Id}' is not reachable from the start node.");
            }
        }

        private void ValidateAcyclicity()
        {
            var adjacency = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            foreach (EdgeInfo edge in edges)
            {
                AddTarget(adjacency, edge.From, edge.To);
            }

            var nodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < nodes.Count; index++)
            {
                nodeIndex[nodes[index].Id] = index;
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            var cycleTargets = new SortedSet<int>();
            foreach (NodeInfo node in nodes)
            {
                DfsForCycle(
                    node.Id,
                    adjacency,
                    state,
                    nodeIndex,
                    cycleTargets);
            }

            foreach (int index in cycleTargets)
            {
                Add(
                    "CYCLE_DETECTED",
                    NodePath(index),
                    $"Node '{nodes[index].Id}' is part of a directed cycle.");
            }
        }

        private static void DfsForCycle(
            string nodeId,
            Dictionary<string, List<string>> adjacency,
            Dictionary<string, int> state,
            Dictionary<string, int> nodeIndex,
            SortedSet<int> cycleTargets)
        {
            if (state.TryGetValue(nodeId, out int current) && current != 0)
            {
                return;
            }

            state[nodeId] = 1;
            if (adjacency.TryGetValue(nodeId, out List<string>? targets))
            {
                foreach (string target in targets)
                {
                    if (!nodeIndex.ContainsKey(target))
                    {
                        continue;
                    }

                    if (state.TryGetValue(target, out int targetState))
                    {
                        if (targetState == 1)
                        {
                            _ = cycleTargets.Add(nodeIndex[target]);
                        }
                    }
                    else
                    {
                        DfsForCycle(
                            target,
                            adjacency,
                            state,
                            nodeIndex,
                            cycleTargets);
                    }
                }
            }

            state[nodeId] = 2;
        }

        private static void AddEdge(
            Dictionary<string, List<EdgeInfo>> map,
            string key,
            EdgeInfo edge)
        {
            if (!map.TryGetValue(key, out List<EdgeInfo>? list))
            {
                list = [];
                map[key] = list;
            }

            list.Add(edge);
        }

        private static void AddTarget(
            Dictionary<string, List<string>> map,
            string key,
            string target)
        {
            if (!map.TryGetValue(key, out List<string>? list))
            {
                list = [];
                map[key] = list;
            }

            list.Add(target);
        }

        private void Add(string code, string path, string message)
        {
            errors.Add($"{code} at {path}: {message}");
        }

        private static string NodePath(int index)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"/nodes/{index}");
        }

        private static string ReadString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : string.Empty;
        }
    }

    private sealed record NodeInfo(
        string Id,
        string Type,
        bool HasFormCode,
        bool HasFormVersion,
        int? DueDays);

    private sealed record EdgeInfo(string From, string To, bool HasCondition);
}
