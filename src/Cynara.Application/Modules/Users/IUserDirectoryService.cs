namespace Cynara.Application.Modules.Users;

/// <summary>
/// Administrative read workflows over identity data, gated by the
/// <c>users.read</c> capability. Grant scope decides breadth (hospital vs.
/// platform). Listing is never audited; the HTTP surface audits successful
/// detail reads after they succeed.
/// </summary>
public interface IUserDirectoryService
{
    /// <summary>Returns one deterministic page of in-scope directory users.</summary>
    public Task<UserDirectoryListResponse> SearchAsync(
        UserDirectorySearchRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the enriched detail of an in-scope user. Unknown and
    /// out-of-scope identifiers raise the same
    /// <see cref="NotFoundException"/> so existence is
    /// never disclosed across scopes.
    /// </summary>
    public Task<UserDirectoryDetail> FindDetailAsync(
        Guid userId,
        Guid? hospitalFilter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Listing request filters for the user directory. The hospital filter
/// carries the stable hospital business code, matching the codes surfaced
/// in membership payloads and the tenant header convention.
/// </summary>
public sealed record UserDirectorySearchRequest(
    string? SearchTerm,
    string? HospitalCode,
    int Page,
    int PageSize);
