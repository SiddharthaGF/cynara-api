namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Validates a <c>ProfileSnapshot</c> payload against the canonical
/// <c>profile-snapshot.schema.json</c> contract. Returns human-readable
/// evaluation errors; an empty list means the payload conforms. Malformed
/// JSON and schema violations share the same error-list shape so callers
/// treat both uniformly.
/// </summary>
public interface IProfileSnapshotValidator
{
    public Task<IReadOnlyList<string>> ValidateAsync(
        string snapshotJson,
        CancellationToken cancellationToken);
}
