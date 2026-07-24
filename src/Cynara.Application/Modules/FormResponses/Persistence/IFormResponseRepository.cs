using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.FormResponses.Persistence;

public interface IFormResponseRepository
{
    public Task<FormVersion?> FindPublishedVersionAsync(
        string code,
        string version,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public void Add(FormResponse response, FormResponseRevision revision);

    public void AddRevision(FormResponseRevision revision);

    public Task<FormResponse?> FindByIdAsync(
        Guid id,
        bool track,
        bool includeDeleted,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task<FormResponseRevision?> FindRevisionAsync(
        Guid responseId,
        uint revisionNumber,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<FormResponseRevision>> ListRevisionsAsync(
        Guid responseId,
        Guid hospitalId,
        CancellationToken cancellationToken);
}
