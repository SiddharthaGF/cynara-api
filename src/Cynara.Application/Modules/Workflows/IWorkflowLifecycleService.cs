using Cynara.Application.Workflows;

namespace Cynara.Application.Modules.Workflows;

public interface IWorkflowLifecycleService
{
    public Task<WorkflowSummaryDto> CreateAsync(
        CreateWorkflowRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<WorkflowVersionDto> UpdateDraftAsync(
        string code,
        UpdateWorkflowDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<WorkflowVersionDto> SubmitForReviewAsync(
        string code,
        SubmitWorkflowDraftForReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<WorkflowVersionDto> WithdrawFromReviewAsync(
        string code,
        WithdrawWorkflowDraftFromReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<WorkflowVersionDto> RejectReviewAsync(
        string code,
        RejectWorkflowReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<WorkflowVersionDto> PublishDraftAsync(
        string code,
        PublishWorkflowDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<WorkflowVersionDto> CreateDraftFromLatestAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<WorkflowVersionDto> RetireVersionAsync(
        string code,
        string version,
        string? actorId,
        CancellationToken cancellationToken);

    public Task SoftDeleteDraftAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken);
}
