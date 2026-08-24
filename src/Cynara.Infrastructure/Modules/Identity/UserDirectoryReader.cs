using Cynara.Application.Modules.Users;
using Cynara.Application.Modules.Users.Persistence;
using Cynara.Domain.Capabilities;

using Microsoft.AspNetCore.Identity;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Read-only implementation of <see cref="IUserDirectoryReader"/> over the
/// identity and domain persistence contexts. Identity drives paging and
/// counting so a multi-hospital user appears once; cross-context joins run
/// in memory, and no read tracks or mutates state.
/// </summary>
public sealed class UserDirectoryReader(
    CynaraIdentityDbContext identity,
    CynaraDbContext domain) : IUserDirectoryReader
{
    /// <inheritdoc />
    /// <remarks>
    /// Scopes via an EXISTS semi-join (no DISTINCT stage) so each user
    /// matches once and the ordered Skip/Take page stays translatable, and
    /// orders entity columns before projection since EF cannot reorder
    /// through constructed types.
    /// </remarks>
    public async Task<DirectoryPage> SearchAsync(
        DirectoryQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<IdentityUser<Guid>> scopedUsers = identity.Users
            .AsNoTracking()
            .Where(user => ScopedMemberships(
                    identity.Memberships.AsNoTracking(),
                    query.PlatformScope,
                    query.ResolvedHospitalId,
                    query.HospitalFilter)
                .Any(membership => membership.UserId == user.Id));

        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            string pattern = BuildContainsPattern(query.SearchTerm);
            scopedUsers = scopedUsers.Where(user =>
                EF.Functions.ILike(user.NormalizedEmail!, pattern, @"\")
                || EF.Functions.ILike(user.NormalizedUserName!, pattern, @"\"));
        }

        int totalCount = await scopedUsers
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        List<UserPageRow> page = await scopedUsers
            .OrderBy(user => user.NormalizedEmail)
            .ThenBy(user => user.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(user => new UserPageRow(
                user.Id,
                user.Email ?? string.Empty,
                user.NormalizedEmail,
                user.NormalizedUserName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (page.Count == 0)
        {
            return new DirectoryPage([], totalCount);
        }

        Guid[] pageIds = [.. page.Select(row => row.UserId)];
        List<Membership> pageMemberships = await ScopedMemberships(
                identity.Memberships.AsNoTracking(),
                query.PlatformScope,
                query.ResolvedHospitalId,
                query.HospitalFilter)
            .Where(item => pageIds.Contains(item.UserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> hospitalCodes = await LoadHospitalCodesAsync(
            [.. pageMemberships.Select(item => item.HospitalId).Distinct()],
            cancellationToken).ConfigureAwait(false);

        var items = new List<UserDirectoryListItem>(page.Count);
        foreach (UserPageRow row in page)
        {
            items.Add(new UserDirectoryListItem(
                row.UserId,
                row.Email,
                [
                    .. pageMemberships
                        .Where(item => item.UserId == row.UserId
                            && hospitalCodes.ContainsKey(item.HospitalId))
                        .Select(item => hospitalCodes[item.HospitalId])
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal),
                ]));
        }

        return new DirectoryPage(items, totalCount);
    }

    /// <inheritdoc />
    public async Task<UserDirectoryDetail?> FindDetailAsync(
        Guid userId,
        DirectoryCallerContext caller,
        Guid? hospitalFilter,
        CancellationToken cancellationToken)
    {
        IdentityUser<Guid>? user = await identity.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        List<Membership> memberships = await ScopedMemberships(
                identity.Memberships.AsNoTracking(),
                caller.PlatformScope,
                caller.ResolvedHospitalId,
                hospitalFilter)
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (memberships.Count == 0)
        {
            return null;
        }

        Dictionary<Guid, string> hospitalCodes = await LoadHospitalCodesAsync(
            [.. memberships.Select(item => item.HospitalId).Distinct()],
            cancellationToken).ConfigureAwait(false);

        string[] capabilities = await LoadEffectiveCapabilitiesAsync(
            memberships,
            cancellationToken).ConfigureAwait(false);

        return new UserDirectoryDetail(
            user.Id,
            user.Email ?? string.Empty,
            user.UserName ?? string.Empty,
            [
                .. memberships
                    .Where(item => hospitalCodes.ContainsKey(item.HospitalId))
                    .Select(item => new UserDirectoryMembershipDto(
                        hospitalCodes[item.HospitalId],
                        item.ActorId,
                        item.CreatedAt)),
            ],
            capabilities,
            new UserDirectoryFlagsDto(
                user.EmailConfirmed,
                user.LockoutEnabled,
                user.LockoutEnd));
    }

    /// <summary>
    /// Restricts memberships to the caller's scope: hospital callers are
    /// pinned to their workspace; only platform scope may narrow by filter.
    /// </summary>
    private static IQueryable<Membership> ScopedMemberships(
        IQueryable<Membership> source,
        bool platformScope,
        Guid resolvedHospitalId,
        Guid? hospitalFilter)
    {
        if (!platformScope)
        {
            return source.Where(item => item.HospitalId == resolvedHospitalId);
        }

        return hospitalFilter is Guid filter
            ? source.Where(item => item.HospitalId == filter)
            : source;
    }

    private async Task<Dictionary<Guid, string>> LoadHospitalCodesAsync(
        Guid[] hospitalIds,
        CancellationToken cancellationToken)
    {
        if (hospitalIds.Length == 0)
        {
            return [];
        }

        List<HospitalCodeRow> rows = await domain.Hospitals
            .AsNoTracking()
            .Where(hospital => hospitalIds.Contains(hospital.Id))
            .Select(hospital => new HospitalCodeRow(
                hospital.Id,
                hospital.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.HospitalId, row => row.Code);
    }

    /// <summary>
    /// Unions capabilities across the target's in-scope actor identities;
    /// platform rows apply globally, hospital rows within their hospital.
    /// </summary>
    private async Task<string[]> LoadEffectiveCapabilitiesAsync(
        IReadOnlyList<Membership> memberships,
        CancellationToken cancellationToken)
    {
        string[] actorIds =
            [.. memberships.Select(item => item.ActorId).Distinct(StringComparer.Ordinal)];
        HashSet<(Guid HospitalId, string ActorId)> inScopePairs =
            [.. memberships.Select(item => (item.HospitalId, item.ActorId))];

        List<CapabilityRow> rows = await domain.CapabilityAssignments
            .AsNoTracking()
            .Where(item => actorIds.Contains(item.ActorId))
            .Select(item => new CapabilityRow(
                item.HospitalId,
                item.ActorId,
                item.Scope,
                item.Capability))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(row => string.Equals(
                    row.Scope,
                    CapabilityScopes.Platform,
                    StringComparison.Ordinal)
                    || inScopePairs.Contains((row.HospitalId, row.ActorId)))
                .Select(row => row.Capability)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string BuildContainsPattern(string searchTerm)
    {
        string escaped = searchTerm
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    /// <summary>Narrow projection feeding the driving distinct-user query.</summary>
    private sealed record UserPageRow(
        Guid UserId,
        string Email,
        string? NormalizedEmail,
        string? NormalizedUserName);

    private sealed record HospitalCodeRow(Guid HospitalId, string Code);

    private sealed record CapabilityRow(
        Guid HospitalId,
        string ActorId,
        string Scope,
        string Capability);
}
