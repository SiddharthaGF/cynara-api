using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Users.Persistence;
using Cynara.Domain.Capabilities;

namespace Cynara.Application.Modules.Users;

/// <summary>
/// Default user-directory workflows. Every call re-checks the resolved
/// tenant context and the <c>users.read</c> capability before resolving
/// the caller's grant scope. The hospital filter narrows platform-scope
/// callers only; an unknown code yields an empty page, never wider results.
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
            && !string.IsNullOrWhiteSpace(request.HospitalCode))
        {
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
    /// Resolves the hospital business code to its identifier for
    /// platform-scope callers only; other callers get
    /// <see langword="null"/> regardless of code. An unknown code returns
    /// a sentinel miss the caller turns into an empty page.
    /// </summary>
    private async Task<Guid?> ResolveHospitalFilterAsync(
        bool platformScope,
        string? hospitalCode,
        CancellationToken cancellationToken)
    {
        if (!platformScope || string.IsNullOrWhiteSpace(hospitalCode))
        {
            return null;
        }

        Domain.Hospitals.Hospital? hospital = await hospitals.FindByCodeAsync(
            hospitalCode.Trim(),
            cancellationToken).ConfigureAwait(false);
        return hospital?.Id;
    }

    /// <summary>
    /// Converts the nullable actor seam value for downstream queries; the
    /// capability guard has already denied empty-actor subjects.
    /// </summary>
    private string RequireActorId()
    {
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
