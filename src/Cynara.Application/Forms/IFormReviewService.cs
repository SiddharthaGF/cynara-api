namespace Cynara.Application.Forms;

public interface IFormReviewService
{
    public Task<FormVersionDto> SubmitForReviewAsync(
        string code,
        SubmitFormDraftForReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<FormVersionDto> WithdrawFromReviewAsync(
        string code,
        WithdrawFormDraftFromReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<FormVersionDto> RejectReviewAsync(
        string code,
        RejectFormReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<FormVersionDto> PublishDraftAsync(
        string code,
        PublishFormDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
