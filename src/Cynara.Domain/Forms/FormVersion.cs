namespace Cynara.Domain.Forms;

public sealed class FormVersion
{
    public Guid Id { get; set; }

    public Guid FormDefinitionId { get; set; }

    public FormDefinition FormDefinition { get; set; } = null!;

    public string? Version { get; set; }

    public FormVersionStatus Status { get; set; }

    public required string ClinicalSchemaJson { get; set; }

    public string? UiSchemaJson { get; set; }

    public string? RulesSchemaJson { get; set; }

    public string? ContentHash { get; set; }

    public string? DependencyMetadataJson { get; set; }

    public uint RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SubmittedForReviewAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? RetiredAt { get; set; }

    public string? PublishedSchemaVersion { get; set; }

    public string? LastReviewComment { get; set; }

    public string? LastReviewDecision { get; set; }

    public DateTimeOffset? LastReviewedAt { get; set; }
}
