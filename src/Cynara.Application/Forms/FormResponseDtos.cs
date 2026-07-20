namespace Cynara.Application.Forms;

public sealed record FormResponseDto(
    Guid Id,
    string FormCode,
    string FormVersion,
    Guid FormVersionId,
    string Status,
    string AnswersJson,
    uint RevisionNumber,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DeletedAt);

public sealed record FormResponseRevisionDto(
    Guid Id,
    Guid FormResponseId,
    uint RevisionNumber,
    string AnswersJson,
    string Status,
    string? ActorId,
    DateTimeOffset CreatedAt);

public sealed record CreateFormResponseRequest(string? AnswersJson = null);

public sealed record UpdateFormResponseRequest(string AnswersJson, uint RowVersion);

public sealed record CompleteFormResponseRequest(uint RowVersion);
