namespace Cynara.Application.Forms;

public sealed record FormSummaryDto(
    string Code,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? EditableVersionId,
    string? EditableStatus,
    uint? EditableRowVersion,
    IReadOnlyList<string> PublishedVersions);

public sealed record FormVersionDto(
    Guid Id,
    string Code,
    string? Version,
    string Status,
    string ClinicalSchemaJson,
    string? UiSchemaJson,
    string? RulesSchemaJson,
    string? ContentHash,
    string? DependencyMetadataJson,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedForReviewAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt,
    string? PublishedSchemaVersion,
    string? LastReviewComment,
    string? LastReviewDecision,
    DateTimeOffset? LastReviewedAt);

public sealed record CreateFormRequest(
    string Code,
    string Name,
    string ClinicalSchemaJson,
    string? UiSchemaJson,
    string? RulesSchemaJson = null);

public sealed record UpdateFormDraftRequest(
    string ClinicalSchemaJson,
    string? UiSchemaJson,
    string? RulesSchemaJson,
    uint RowVersion);

public sealed record PublishFormDraftRequest(uint RowVersion);

public sealed record SubmitFormDraftForReviewRequest(uint RowVersion);

public sealed record WithdrawFormDraftFromReviewRequest(uint RowVersion);

public sealed record RejectFormReviewRequest(string Comment, uint RowVersion);
