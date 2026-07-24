namespace Cynara.Application.Forms;

public interface IFormService
{
    public Task<FormSummaryDto> CreateAsync(CreateFormRequest request, string? actorId, CancellationToken cancellationToken);

    public Task<IReadOnlyList<FormSummaryDto>> ListAsync(CancellationToken cancellationToken);

    public Task<FormSummaryDto> GetSummaryAsync(string code, CancellationToken cancellationToken);

    public Task<FormVersionDto> GetEditableVersionAsync(string code, CancellationToken cancellationToken);

    public Task<FormVersionDto> GetVersionAsync(string code, string version, CancellationToken cancellationToken);

    public Task<FormVersionDto> UpdateDraftAsync(string code, UpdateFormDraftRequest request, string? actorId, CancellationToken cancellationToken);

    public Task<FormVersionDto> CreateDraftFromLatestAsync(string code, string? actorId, CancellationToken cancellationToken);

    public Task<FormVersionDto> RetireVersionAsync(string code, string version, string? actorId, CancellationToken cancellationToken);

    public Task SoftDeleteDraftAsync(string code, string? reason, string? actorId, CancellationToken cancellationToken);
}
