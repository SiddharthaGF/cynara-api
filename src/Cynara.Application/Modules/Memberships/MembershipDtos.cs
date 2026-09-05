using Cynara.Domain.Memberships;

namespace Cynara.Application.Modules.Memberships;

/// <summary>
/// Application-side projection of a membership period row. The
/// Application layer never references the Infrastructure entity; tracked
/// mutations stay encapsulated in the repository implementation.
/// </summary>
public sealed record MembershipRow(
    Guid Id,
    Guid UserId,
    Guid HospitalId,
    string ActorId,
    MembershipStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ActivatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset UpdatedAt,
    uint RowVersion);

/// <summary>
/// Wire view of a membership: lifecycle metadata without the
/// concurrency token.
/// </summary>
public sealed record MembershipView(
    Guid Id,
    Guid UserId,
    string ActorId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ActivatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset UpdatedAt);

public sealed record AddMembershipRequest(Guid UserId, string ActorId);

public sealed record UpdateMembershipRequest(string ActorId);

public sealed record ReactivateMembershipRequest(string ActorId);
