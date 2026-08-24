using System.Text.Json;

using Cynara.Application.Workflows;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

internal static class WorkflowMappers
{
    public static WorkflowSummaryDto ToSummary(
        WorkflowDefinition definition)
    {
        WorkflowVersion? editable = definition.Versions.SingleOrDefault(
            item => item.Status is WorkflowVersionStatus.Draft
                or WorkflowVersionStatus.Review);
        var publishedVersions = definition.Versions
            .Where(item => item.Status == WorkflowVersionStatus.Published
                && item.Version != null)
            .Select(item => item.Version!)
            .Order(SemverRules.StringComparer)
            .ToList();

        return new WorkflowSummaryDto(
            definition.Code,
            definition.Name,
            definition.CreatedAt,
            definition.UpdatedAt,
            editable?.Id.ToString(),
            editable?.Status.ToString().ToLowerInvariant(),
            editable?.RowVersion,
            publishedVersions);
    }

    public static WorkflowVersionDto ToVersionDto(
        WorkflowDefinition definition,
        WorkflowVersion version)
    {
        return new WorkflowVersionDto(
            version.Id,
            definition.Code,
            version.Version,
            version.Status.ToString().ToLowerInvariant(),
            version.WorkflowSchemaJson,
            version.ContentHash,
            version.RowVersion,
            version.CreatedAt,
            version.SubmittedForReviewAt,
            version.PublishedAt,
            version.RetiredAt,
            version.PublishedSchemaVersion,
            version.LastReviewComment,
            version.LastReviewDecision,
            version.LastReviewedAt);
    }

    public static string ReadSchemaVersion(string workflowSchemaJson)
    {
        using var document = JsonDocument.Parse(workflowSchemaJson);
        return document.RootElement.TryGetProperty(
                "schemaVersion",
                out JsonElement schemaVersion)
            ? schemaVersion.GetString() ?? "1.0.0"
            : "1.0.0";
    }
}
