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
/// a hospital-scoped caller's view.
/// </summary>
public sealed class UserDirectoryService(
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard,
    ICurrentActor currentActor,
    ICapabilityAssignmentRepository assignments,
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

        DirectoryPage result = await reader.SearchAsync(
            new DirectoryQuery(
                platformScope,
                hospitalContext.HospitalId,
                searchTerm,
                platformScope ? request.HospitalFilter : null,
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
