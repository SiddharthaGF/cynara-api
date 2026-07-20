namespace Cynara.Domain.Components;

public sealed class ComponentDefinition
{
    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<ComponentVersion> Versions { get; set; } = [];
}
