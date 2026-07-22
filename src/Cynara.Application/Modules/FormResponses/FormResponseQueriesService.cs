using Cynara.Application.Forms;
using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.FormResponses;

public sealed class FormResponseQueriesService(
    IFormResponseRepository responses) : IFormResponseQueryService
{
    public async Task<FormResponseDto> GetAsync(
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        FormResponse response = await FormResponseWorkflowHelpers
            .RequireResponseAsync(
                responses,
                id,
                false,
                includeDeleted,
                cancellationToken).ConfigureAwait(false);
        return FormResponseMappers.ToDto(response, response.FormVersion);
    }

    public async Task<IReadOnlyList<FormResponseRevisionDto>> ListRevisionsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        _ = await FormResponseWorkflowHelpers.RequireResponseAsync(
            responses,
            id,
            false,
            true,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<FormResponseRevision> revisions = await responses
            .ListRevisionsAsync(id, cancellationToken).ConfigureAwait(false);
        return [.. revisions.Select(FormResponseMappers.ToRevisionDto)];
    }

    public async Task<FormResponseRevisionDto> GetRevisionAsync(
        Guid id,
        uint revisionNumber,
        CancellationToken cancellationToken)
    {
        _ = await FormResponseWorkflowHelpers.RequireResponseAsync(
            responses,
            id,
            false,
            true,
            cancellationToken).ConfigureAwait(false);
        FormResponseRevision revision = await responses.FindRevisionAsync(
                id,
                revisionNumber,
                cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Revision {revisionNumber} for response '{id}' was not found.");
        return FormResponseMappers.ToRevisionDto(revision);
    }
}
