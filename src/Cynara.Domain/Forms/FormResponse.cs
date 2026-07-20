namespace Cynara.Domain.Forms;

public sealed class FormResponse
{
    public Guid Id { get; set; }

    public Guid FormVersionId { get; set; }

    public FormVersion FormVersion { get; set; } = null!;

    public FormResponseStatus Status { get; set; }

    public required string AnswersJson { get; set; }

    public uint RevisionNumber { get; set; }

    public uint RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<FormResponseRevision> Revisions { get; set; } = [];
}
