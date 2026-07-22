using Cynara.Application.Forms;

namespace Cynara.Application.Modules.FormResponses;

public interface IFormResponseLifecycleService
{
    public Task<FormResponseDto> CreateAsync(
        string code,
        string version,
        CreateFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken);

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

    public Task SoftDeleteDraftAsync(
        Guid id,
        string? reason,
        string? actorId,
        CancellationToken cancellationToken);
}
