namespace Cynara.Domain.Forms;

public sealed class FormResponseRevision
{
    public Guid Id { get; set; }

    public Guid FormResponseId { get; set; }

    public FormResponse FormResponse { get; set; } = null!;

    public uint RevisionNumber { get; set; }

    public required string AnswersJson { get; set; }

    public FormResponseStatus Status { get; set; }

    public string? ActorId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
