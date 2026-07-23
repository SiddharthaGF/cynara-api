namespace Cynara.Domain.Components;

public sealed class ComponentVersion
{
    public Guid Id { get; set; }

    public Guid ComponentDefinitionId { get; set; }

    public ComponentDefinition ComponentDefinition { get; set; } = null!;

    public string? Version { get; set; }

    public ComponentVersionStatus Status { get; set; }

    public required string ClinicalSchemaJson { get; set; }

    public string? UiSchemaJson { get; set; }

    public string? ContentHash { get; set; }

    public uint RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? RetiredAt { get; set; }
}
