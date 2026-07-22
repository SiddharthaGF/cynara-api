namespace Cynara.Domain.Forms;

public sealed class FormDefinition
{
    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<FormVersion> Versions { get; } = [];
}
