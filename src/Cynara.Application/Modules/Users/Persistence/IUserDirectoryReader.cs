namespace Cynara.Application.Modules.Users.Persistence;

/// <summary>
/// Read-only persistence port for the administrative user directory. The
/// identity context drives paging/counting (one row per multi-hospital
/// user); hospital codes and capability rows come from bounded untracked
/// follow-ups. Never tracks, stages, or commits: reads are non-mutating.
/// </summary>
public interface IUserDirectoryReader
{
    /// <summary>
    /// Returns one deterministic page of distinct in-scope users plus the
    /// total count matching the query, ordered by normalized email then id;
    /// the count uses the same driving query so totals stay stable.
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
/// platform-scope and the caller's resolved hospital (the only visible one
/// otherwise). Platform callers' hospital filters narrow results; they never
/// widen a hospital-scoped view.
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
