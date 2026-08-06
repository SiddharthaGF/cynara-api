namespace Cynara.Application.Workflows;

public sealed record WorkflowSummaryDto(
    string Code,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? EditableVersionId,
    string? EditableStatus,
    uint? EditableRowVersion,
    IReadOnlyList<string> PublishedVersions);

public sealed record WorkflowVersionDto(
    Guid Id,
    string Code,
    string? Version,
    string Status,
    string WorkflowSchemaJson,
    string? ContentHash,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedForReviewAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt,
    string? PublishedSchemaVersion,
    string? LastReviewComment,
    string? LastReviewDecision,
    DateTimeOffset? LastReviewedAt);

public sealed record CreateWorkflowRequest(
    string Code,
    string Name,
    string WorkflowSchemaJson);

public sealed record UpdateWorkflowDraftRequest(
    string WorkflowSchemaJson,
    uint RowVersion);

public sealed record PublishWorkflowDraftRequest(uint RowVersion);

public sealed record SubmitWorkflowDraftForReviewRequest(uint RowVersion);

public sealed record WithdrawWorkflowDraftFromReviewRequest(uint RowVersion);

public sealed record RejectWorkflowReviewRequest(string Comment, uint RowVersion);
