using Cynara.Application.Modules.Components.Persistence;
using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components;

internal static class ComponentWorkflowHelpers
{
    public static async Task<ComponentDefinition> RequireDefinitionAsync(
        IComponentRepository components,
        string code,
        bool track,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        ComponentDefinition? definition = await components
            .FindDefinitionByCodeAsync(code, hospitalId, track, cancellationToken).ConfigureAwait(false);
        return definition ?? throw new NotFoundException(
            $"Component '{code}' was not found.");
    }

    public static ComponentVersion RequireDraft(
        ComponentDefinition definition)
    {
        return definition.Versions.SingleOrDefault(
                item => item.Status == ComponentVersionStatus.Draft)
            ?? throw new NotFoundException(
                $"Component '{definition.Code}' has no draft version.");
    }

    public static void EnsureDraftConcurrency(
        ComponentVersion draft,
        uint expectedRowVersion)
    {
        if (draft.RowVersion != expectedRowVersion)
        {
            throw new ConcurrencyException(
                "The component draft was modified by another request.");
        }
    }
}
