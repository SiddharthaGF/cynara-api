using Cynara.Application.Forms;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.FormResponses;

public sealed class FormResponseQueriesService(
    IFormResponseRepository responses,
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard) : IFormResponseQueryService
{
    public async Task<FormResponseDto> GetAsync(
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesRead, cancellationToken)
            .ConfigureAwait(false);
        FormResponse response = await FormResponseWorkflowHelpers
            .RequireResponseAsync(
                responses,
                id,
                track: false,
                includeDeleted,
                hospitalContext.HospitalId,
                cancellationToken).ConfigureAwait(false);
        return FormResponseMappers.ToDto(response, response.FormVersion);
    }

    public async Task<IReadOnlyList<FormResponseRevisionDto>> ListRevisionsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesRead, cancellationToken)
            .ConfigureAwait(false);
        _ = await FormResponseWorkflowHelpers.RequireResponseAsync(
            responses,
            id,
            track: false,
            includeDeleted: true,
            hospitalContext.HospitalId,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<FormResponseRevision> revisions = await responses
            .ListRevisionsAsync(id, hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        return [.. revisions.Select(FormResponseMappers.ToRevisionDto)];
    }

    public async Task<FormResponseRevisionDto> GetRevisionAsync(
        Guid id,
        uint revisionNumber,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesRead, cancellationToken)
            .ConfigureAwait(false);
        _ = await FormResponseWorkflowHelpers.RequireResponseAsync(
            responses,
            id,
            track: false,
            includeDeleted: true,
            hospitalContext.HospitalId,
            cancellationToken).ConfigureAwait(false);
        FormResponseRevision revision = await responses.FindRevisionAsync(
                id,
                revisionNumber,
                hospitalContext.HospitalId,
                cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Revision {revisionNumber} for response '{id}' was not found.");
        return FormResponseMappers.ToRevisionDto(revision);
    }
}
