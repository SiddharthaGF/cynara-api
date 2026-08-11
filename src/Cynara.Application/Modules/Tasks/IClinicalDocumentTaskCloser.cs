namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Completes open clinical tasks whose form matches a just-completed
/// clinical document. Owned by the Tasks module so the document lifecycle
/// never mutates the task aggregate directly; stages through the same unit
/// of work as the calling workflow.
/// </summary>
public interface IClinicalDocumentTaskCloser
{
    public Task CloseOpenTasksForCompletedDocumentAsync(
        Guid hospitalId,
        Guid? encounterId,
        string formCode,
        Guid clinicalDocumentId,
        string? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
