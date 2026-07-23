using System.Text.Json;

using Cynara.Application.Common;
using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

internal static class FormMappers
{
    public static FormSummaryDto ToSummary(FormDefinition definition)
    {
        FormVersion? editable = definition.Versions.SingleOrDefault(
            item => item.Status is FormVersionStatus.Draft
                or FormVersionStatus.Review);
        var publishedVersions = definition.Versions
            .Where(item => item.Status == FormVersionStatus.Published
                && item.Version != null)
            .Select(item => item.Version!)
            .Order(SemverRules.StringComparer)
            .ToList();

        return new FormSummaryDto(
            definition.Code,
            definition.Name,
            definition.CreatedAt,
            definition.UpdatedAt,
            editable?.Id.ToString(),
            editable?.Status.ToString().ToLowerInvariant(),
            editable?.RowVersion,
            publishedVersions);
    }

    public static FormVersionDto ToVersionDto(
        FormDefinition definition,
        FormVersion version)
    {
        return new FormVersionDto(
            version.Id,
            definition.Code,
            version.Version,
            version.Status.ToString().ToLowerInvariant(),
            version.ClinicalSchemaJson,
            version.UiSchemaJson,
            version.RulesSchemaJson,
            version.ContentHash,
            version.DependencyMetadataJson,
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

    public static string ReadSchemaVersion(string clinicalSchemaJson)
    {
        using var document = JsonDocument.Parse(clinicalSchemaJson);
        return document.RootElement.TryGetProperty(
                "schemaVersion",
                out JsonElement schemaVersion)
            ? schemaVersion.GetString() ?? "1.0.0"
            : "1.0.0";
    }
}
