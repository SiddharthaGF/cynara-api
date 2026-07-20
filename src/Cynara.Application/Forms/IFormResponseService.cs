namespace Cynara.Application.Forms;

public interface IFormResponseService
{
    public Task<FormResponseDto> CreateAsync(
        string code,
        string version,
        CreateFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<FormResponseDto> GetAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken);

    public Task<FormResponseDto> UpdateAsync(
        Guid id,
        UpdateFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<FormResponseDto> CompleteAsync(
        Guid id,
        CompleteFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task SoftDeleteDraftAsync(Guid id, string? reason, string? actorId, CancellationToken cancellationToken);

    public Task<IReadOnlyList<FormResponseRevisionDto>> ListRevisionsAsync(
        Guid id,
        CancellationToken cancellationToken);

    public Task<FormResponseRevisionDto> GetRevisionAsync(
        Guid id,
        uint revisionNumber,
        CancellationToken cancellationToken);
}
