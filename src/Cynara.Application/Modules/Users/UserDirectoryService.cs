using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Users.Persistence;
using Cynara.Domain.Capabilities;

namespace Cynara.Application.Modules.Users;

/// <summary>
/// Default user-directory workflows. Every call re-checks the resolved
/// tenant context and the <c>users.read</c> capability so denial never
/// depends on route wiring alone, then resolves the caller's grant scope to
/// decide listing breadth. The hospital filter is honored exclusively for
/// platform-scope callers: it narrows their global view but can never widen
/// a hospital-scoped caller's view. The filter carries a hospital business
/// code; an unknown code yields an empty page rather than falling back to an
/// unfiltered listing, so a bad reference can never widen results.
/// </summary>
public sealed class UserDirectoryService(
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard,
    ICurrentActor currentActor,
    ICapabilityAssignmentRepository assignments,
    IHospitalRepository hospitals,
    IUserDirectoryReader reader) : IUserDirectoryService
{
    /// <inheritdoc />
    public async Task<UserDirectoryListResponse> SearchAsync(
        UserDirectorySearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.UsersRead,
            cancellationToken).ConfigureAwait(false);
        string actorId = RequireActorId();
        bool platformScope = await ResolvePlatformScopeAsync(
            actorId,
            cancellationToken).ConfigureAwait(false);

        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize < 1
            ? UserDirectoryFieldLimits.DefaultPageSize
            : Math.Min(request.PageSize, UserDirectoryFieldLimits.MaxPageSize);
        string? searchTerm = NormalizeSearchTerm(request.SearchTerm);
        Guid? hospitalFilter = await ResolveHospitalFilterAsync(
            platformScope,
            request.HospitalCode,
            cancellationToken).ConfigureAwait(false);
        if (platformScope
            && hospitalFilter is null
            && HasHospitalCode(request.HospitalCode))
        {
            // A platform caller named a hospital that does not exist: no
            // members can match, and the empty page must not degrade into an
            // unfiltered listing.
            return new UserDirectoryListResponse([], page, pageSize, 0);
        }

        DirectoryPage result = await reader.SearchAsync(
            new DirectoryQuery(
                platformScope,
                hospitalContext.HospitalId,
                searchTerm,
                hospitalFilter,
                page,
                pageSize),
            cancellationToken).ConfigureAwait(false);

        return new UserDirectoryListResponse(
            result.Items,
            page,
            pageSize,
            result.TotalCount);
    }

    /// <inheritdoc />
    public async Task<UserDirectoryDetail> FindDetailAsync(
        Guid userId,
        Guid? hospitalFilter,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.UsersRead,
            cancellationToken).ConfigureAwait(false);
        string actorId = RequireActorId();
        bool platformScope = await ResolvePlatformScopeAsync(
            actorId,
            cancellationToken).ConfigureAwait(false);

        UserDirectoryDetail? detail = await reader.FindDetailAsync(
            userId,
            new DirectoryCallerContext(platformScope, hospitalContext.HospitalId),
            platformScope ? hospitalFilter : null,
            cancellationToken).ConfigureAwait(false);

        return detail
            ?? throw new NotFoundException($"User '{userId}' was not found.");
    }

    private async Task<bool> ResolvePlatformScopeAsync(
        string actorId,
        CancellationToken cancellationToken)
    {
        return await assignments.HasPlatformScopeAsync(
            actorId,
            CapabilityCodes.UsersRead,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the hospital business code to its identifier. Only
    /// platform-scope callers may narrow by hospital, so a hospital-scoped
    /// request yields <see langword="null"/> regardless of the supplied
    /// code; an unknown platform-side code returns a sentinel miss that the
    /// caller turns into an empty page.
    /// </summary>
    private async Task<Guid?> ResolveHospitalFilterAsync(
        bool platformScope,
        string? hospitalCode,
        CancellationToken cancellationToken)
    {
        if (!platformScope || !HasHospitalCode(hospitalCode))
        {
            return null;
        }

        Domain.Hospitals.Hospital? hospital = await hospitals.FindByCodeAsync(
            hospitalCode.Trim(),
            cancellationToken).ConfigureAwait(false);
        return hospital?.Id;
    }

    private static bool HasHospitalCode(string? hospitalCode)
    {
        return !string.IsNullOrWhiteSpace(hospitalCode);
    }

    private string RequireActorId()
    {
        // The capability guard already denies empty-actor subjects, so this
        // only converts the nullable seam value for downstream queries.
        return currentActor.ActorId
            ?? throw new InvalidStateException(
                "An actor identity is required for directory reads.");
    }

    private static string? NormalizeSearchTerm(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return null;
        }

        return searchTerm.Trim();
    }
}
