using Cynara.Application.Components;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Components.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components;

public sealed class ComponentQueriesService(
    IComponentRepository components,
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard) : IComponentQueryService
{
    public async Task<IReadOnlyList<ComponentSummaryDto>> ListAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<ComponentDefinition> items = await components
            .ListDefinitionsAsync(hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        return [.. items.Select(ComponentMappers.ToSummary)];
    }

    public async Task<ComponentSummaryDto> GetSummaryAsync(
        string code,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, track: false, hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        return ComponentMappers.ToSummary(definition);
    }

    public async Task<ComponentVersionDto> GetDraftAsync(
        string code,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, track: false, hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        ComponentVersion draft = ComponentWorkflowHelpers.RequireDraft(definition);
        return ComponentMappers.ToVersionDto(definition, draft);
    }
}
