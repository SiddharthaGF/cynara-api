namespace Cynara.Application.Components;

public sealed record ComponentSummaryDto(
    string Code,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? DraftVersionId,
    uint? DraftRowVersion,
    IReadOnlyList<string> PublishedVersions);

public sealed record ComponentVersionDto(
    Guid Id,
    string Code,
    string? Version,
    string Status,
    string ClinicalSchemaJson,
    string? UiSchemaJson,
    string? ContentHash,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt);

public sealed record CreateComponentRequest(
    string Code,
    string Name,
    string ClinicalSchemaJson,
    string? UiSchemaJson);

public sealed record UpdateComponentDraftRequest(
    string ClinicalSchemaJson,
    string? UiSchemaJson,
    uint RowVersion);

public sealed record PublishComponentDraftRequest(uint RowVersion);
