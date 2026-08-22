namespace Cynara.Application.Modules.Users.Persistence;

/// <summary>
/// Read-only persistence port for the administrative user directory. The
/// identity context (users + memberships) drives paging and counting so a
/// multi-hospital user is one row; hospital display codes and capability
/// rows live on the domain context and are fetched with bounded untracked
/// follow-ups. The reader never tracks entities, stages changes, or commits:
/// directory reads are non-mutating workflows.
/// </summary>
public interface IUserDirectoryReader
{
    /// <summary>
    /// Returns one deterministic page of distinct in-scope users plus the
    /// total count of distinct in-scope users matching the query. Ordering
    /// is normalized email then user id; the count is computed from the same
    /// driving query as the page so totals stay stable across pages.
    /// </summary>
    public Task<DirectoryPage> SearchAsync(
        DirectoryQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the enriched detail for an in-scope user, or
    /// <see langword="null"/> when the identifier is unknown OR outside the
    /// caller's scope. Both cases collapse to the same <see langword="null"/>
    /// result so callers cannot distinguish them (anti-enumeration).
    /// </summary>
    public Task<UserDirectoryDetail?> FindDetailAsync(
        Guid userId,
        DirectoryCallerContext caller,
        Guid? hospitalFilter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Caller scope context for a directory read: whether the caller's grant is
/// platform-scope (spans every hospital) and the caller's resolved hospital
/// (the only visible hospital otherwise). Hospital filters supplied by
/// platform-scope callers narrow results; they never widen a
/// hospital-scoped caller's view.
/// </summary>
public sealed record DirectoryCallerContext(
    bool PlatformScope,
    Guid ResolvedHospitalId);

/// <summary>
/// One directory listing request. Paging happens on the distinct-user
/// projection of the driving identity-context query.
/// </summary>
public sealed record DirectoryQuery(
    bool PlatformScope,
    Guid ResolvedHospitalId,
    string? SearchTerm,
    Guid? HospitalFilter,
    int Page,
    int PageSize);

/// <summary>One page of directory items plus its stable total count.</summary>
public sealed record DirectoryPage(
    IReadOnlyList<UserDirectoryListItem> Items,
    int TotalCount);
