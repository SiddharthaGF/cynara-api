using Cynara.Application.Components;
using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components;

internal static class ComponentMappers
{
    public static ComponentSummaryDto ToSummary(
        ComponentDefinition definition)
    {
        ComponentVersion? draft = definition.Versions.SingleOrDefault(
            item => item.Status == ComponentVersionStatus.Draft);
        var publishedVersions = definition.Versions
            .Where(item => item.Status == ComponentVersionStatus.Published
                && item.Version != null)
            .Select(item => item.Version!)
            .Order(SemverRules.StringComparer)
            .ToList();

        return new ComponentSummaryDto(
            definition.Code,
            definition.Name,
            definition.CreatedAt,
            definition.UpdatedAt,
            draft?.Id.ToString(),
            draft?.RowVersion,
            publishedVersions);
    }

    public static ComponentVersionDto ToVersionDto(
        ComponentDefinition definition,
        ComponentVersion version)
    {
        return new ComponentVersionDto(
            version.Id,
            definition.Code,
            version.Version,
            version.Status.ToString().ToLowerInvariant(),
            version.ClinicalSchemaJson,
            version.UiSchemaJson,
            version.ContentHash,
            version.RowVersion,
            version.CreatedAt,
            version.PublishedAt,
            version.RetiredAt);
    }
}
