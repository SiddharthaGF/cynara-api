namespace Cynara.Application.Modules.Users;

/// <summary>
/// Wire shapes of the administrative user directory. The list item exposes
/// exactly the identifier, email, and in-scope membership hospitals; the
/// detail adds the user name, membership records, capability codes, and
/// Identity account flags. No roles field exists anywhere: roles are out of
/// scope for this increment and must never appear as invented placeholders.
/// </summary>
public sealed record UserDirectoryListItem(
    Guid Id,
    string Email,
    IReadOnlyList<string> Hospitals);

/// <summary>
/// Patients-style numeric page envelope for the directory listing.
/// </summary>
public sealed record UserDirectoryListResponse(
    IReadOnlyList<UserDirectoryListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>One in-scope membership record on a directory detail.</summary>
public sealed record UserDirectoryMembershipDto(
    string Hospital,
    string ActorId,
    DateTimeOffset CreatedAt);

/// <summary>Identity account flags surfaced by a directory detail.</summary>
public sealed record UserDirectoryFlagsDto(
    bool EmailConfirmed,
    bool LockoutEnabled,
    DateTimeOffset? LockoutEnd);

/// <summary>
/// Enriched detail payload. Memberships and capabilities cover only what is
/// inside the caller's scope; capabilities are the union of the target's
/// in-scope actor identities' effective grants.
/// </summary>
public sealed record UserDirectoryDetail(
    Guid Id,
    string Email,
    string UserName,
    IReadOnlyList<UserDirectoryMembershipDto> Memberships,
    IReadOnlyList<string> Capabilities,
    UserDirectoryFlagsDto Flags);
