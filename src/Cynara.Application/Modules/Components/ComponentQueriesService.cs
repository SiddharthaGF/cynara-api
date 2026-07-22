using Cynara.Application.Common;
using Cynara.Application.Components;
using Cynara.Application.Modules.Components.Persistence;
using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components;

public sealed class ComponentQueriesService(
    IComponentRepository components) : IComponentQueryService
{
    public async Task<IReadOnlyList<ComponentSummaryDto>> ListAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ComponentDefinition> items = await components
            .ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        return [.. items.Select(ComponentMappers.ToSummary)];
    }

    public async Task<ComponentSummaryDto> GetSummaryAsync(
        string code,
        CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, false, cancellationToken).ConfigureAwait(false);
        return ComponentMappers.ToSummary(definition);
    }

    public async Task<ComponentVersionDto> GetDraftAsync(
        string code,
        CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, false, cancellationToken).ConfigureAwait(false);
        ComponentVersion draft = ComponentWorkflowHelpers.RequireDraft(definition);
        return ComponentMappers.ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> GetVersionAsync(
        string code,
        string version,
        CancellationToken cancellationToken)
    {
        SemverRules.EnsureValid(version);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, false, cancellationToken).ConfigureAwait(false);
        ComponentVersion published = definition.Versions.SingleOrDefault(
                item => item.Version == version
                    && item.Status != ComponentVersionStatus.Draft)
            ?? throw new NotFoundException(
                $"Component '{code}' version '{version}' was not found.");
        return ComponentMappers.ToVersionDto(definition, published);
    }
}
