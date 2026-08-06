namespace Cynara.Api.Tests.Workflows;

internal static class WorkflowTestSchemas
{
    public static string Minimal()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Workflow starts" },
                { "id": "end", "type": "end", "name": "Completed" }
              ],
              "edges": [
                { "from": "start", "to": "end", "label": "Begin" }
              ]
            }
            """;
    }

    public static string WithDecision()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "inputs": ["triage-score"],
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "triage", "type": "decision", "name": "Triage assessment" },
                {
                  "id": "low-task",
                  "type": "task",
                  "name": "Low risk follow-up",
                  "assignee": { "role": "nurse" }
                },
                {
                  "id": "high-task",
                  "type": "task",
                  "name": "High risk review",
                  "assignee": { "role": "physician" }
                },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "triage" },
                {
                  "from": "triage",
                  "to": "low-task",
                  "condition": {
                    "op": "lte",
                    "args": [ { "ref": "triage-score" }, { "lit": 5 } ]
                  }
                },
                { "from": "triage", "to": "high-task", "label": "Default" },
                { "from": "low-task", "to": "end" },
                { "from": "high-task", "to": "end" }
              ]
            }
            """;
    }

    public static string WithPinnedFormTask(string? formVersion = "1.0.0")
    {
        return formVersion is null
            ? /*lang=json,strict*/ """
                {
                  "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
                  "schemaVersion": "1.0.0",
                  "nodes": [
                    { "id": "start", "type": "start", "name": "Begin" },
                    {
                      "id": "collect",
                      "type": "task",
                      "name": "Collect intake",
                      "formCode": "intake-assessment",
                      "assignee": { "role": "nurse" }
                    },
                    { "id": "end", "type": "end", "name": "Done" }
                  ],
                  "edges": [
                    { "from": "start", "to": "collect" },
                    { "from": "collect", "to": "end" }
                  ]
                }
                """
            : /*lang=json,strict*/ $$"""
                {
                  "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
                  "schemaVersion": "1.0.0",
                  "nodes": [
                    { "id": "start", "type": "start", "name": "Begin" },
                    {
                      "id": "collect",
                      "type": "task",
                      "name": "Collect intake",
                      "formCode": "intake-assessment",
                      "formVersion": "{{formVersion}}",
                      "assignee": { "role": "nurse" }
                    },
                    { "id": "end", "type": "end", "name": "Done" }
                  ],
                  "edges": [
                    { "from": "start", "to": "collect" },
                    { "from": "collect", "to": "end" }
                  ]
                }
                """;
    }

    public static string WithMissingStart()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "task", "type": "task", "name": "Collect" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "task", "to": "end" }
              ]
            }
            """;
    }

    public static string WithMissingEnd()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "task", "type": "task", "name": "Collect" }
              ],
              "edges": [
                { "from": "start", "to": "task" }
              ]
            }
            """;
    }

    public static string WithDuplicateNodeId()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "dup", "type": "task", "name": "One" },
                { "id": "dup", "type": "task", "name": "Two" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "dup" },
                { "from": "dup", "to": "end" }
              ]
            }
            """;
    }

    public static string WithCycle()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "loop-a", "type": "task", "name": "Loop A" },
                { "id": "loop-b", "type": "task", "name": "Loop B" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "loop-a" },
                { "from": "loop-a", "to": "loop-b" },
                { "from": "loop-b", "to": "loop-a" },
                { "from": "loop-b", "to": "end" }
              ]
            }
            """;
    }

    public static string WithUnreachableNode()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "orphan", "type": "task", "name": "Orphaned" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "end" }
              ]
            }
            """;
    }

    public static string WithUnknownEdgeNode()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "ghost" }
              ]
            }
            """;
    }

    public static string WithUnknownConditionRef()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "inputs": ["known-input"],
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "triage", "type": "decision", "name": "Triage" },
                { "id": "task-a", "type": "task", "name": "A" },
                { "id": "task-b", "type": "task", "name": "B" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "triage" },
                {
                  "from": "triage",
                  "to": "task-a",
                  "condition": {
                    "op": "eq",
                    "args": [ { "ref": "undeclared-input" }, { "lit": "yes" } ]
                  }
                },
                { "from": "triage", "to": "task-b", "label": "Default" },
                { "from": "task-a", "to": "end" },
                { "from": "task-b", "to": "end" }
              ]
            }
            """;
    }

    public static string WithStartIncomingEdge()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "task", "type": "task", "name": "Collect" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "task" },
                { "from": "task", "to": "start" },
                { "from": "start", "to": "end" }
              ]
            }
            """;
    }

    public static string WithTaskConditionalOutput()
    {
        return /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "inputs": ["anything"],
              "nodes": [
                { "id": "start", "type": "start", "name": "Begin" },
                { "id": "task", "type": "task", "name": "Collect" },
                { "id": "end", "type": "end", "name": "Done" }
              ],
              "edges": [
                { "from": "start", "to": "task" },
                {
                  "from": "task",
                  "to": "end",
                  "condition": {
                    "op": "eq",
                    "args": [ { "ref": "anything" }, { "lit": "yes" } ]
                  }
                }
              ]
            }
            """;
    }
}
