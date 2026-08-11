using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Domain.Forms;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IFormResponseRepository"/> limited to the
/// surface the document start workflow actually calls. The fake records
/// added responses so tests can assert the bound response was created with
/// the published snapshot and an initial revision.
/// </summary>
public sealed class FakeFormResponseRepository : IFormResponseRepository
{
    private readonly List<FormResponse> responses = [];

    private readonly List<(FormResponse Response, FormResponseRevision Revision)>
        added = [];

    private readonly List<FormResponseRevision> revisions = [];

    public IReadOnlyCollection<FormResponse> Responses => responses;

    public IReadOnlyCollection<FormResponseRevision> AddedRevisions =>
        [.. added.Select(item => item.Revision).Concat(revisions)];

    public void Seed(params FormResponse[] seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);
        responses.AddRange(seeded);
    }

    public Task<FormVersion?> FindPublishedVersionAsync(
        string code,
        string version,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<FormVersion?>(null);
    }

    public void Add(FormResponse response, FormResponseRevision revision)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(revision);
        added.Add((response, revision));
        responses.Add(response);
    }

    public void AddRevision(FormResponseRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        revisions.Add(revision);
    }

    public Task<FormResponse?> FindByIdAsync(
        Guid id,
        bool track,
        bool includeDeleted,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        FormResponse? match = responses.SingleOrDefault(
            item => item.Id == id && item.HospitalId == hospitalId);
        return Task.FromResult(match);
    }
}
