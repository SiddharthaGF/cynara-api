using Cynara.Application.Forms;

namespace Cynara.Application.Modules.FormResponses;

public interface IFormResponseQueryService
{
    public Task<FormResponseDto> GetAsync(
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<FormResponseRevisionDto>> ListRevisionsAsync(
        Guid id,
        CancellationToken cancellationToken);

    public Task<FormResponseRevisionDto> GetRevisionAsync(
        Guid id,
        uint revisionNumber,
        CancellationToken cancellationToken);
}
