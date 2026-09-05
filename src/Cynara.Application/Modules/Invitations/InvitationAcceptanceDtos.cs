using System.Text.Json.Serialization;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Body of the public acceptance request: credentials plus the member's
/// given/family names when the invitation snapshot did not predefine them.
/// </summary>
public sealed record AcceptInvitationRequest(
    string? Password,
    string? Name,
    string? Surname);

/// <summary>
/// Uniform wire envelope for every acceptance outcome. Token-state failures
/// serialize to the byte-identical <c>{"accepted":false}</c> body; success
/// adds the member summary. The member payload is omitted when
/// <see cref="Accepted"/> is <see langword="false"/> so the failure body is
/// constant by construction and never leaks token or hash material.
/// </summary>
public sealed record AcceptInvitationResponse(
    bool Accepted,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    MemberSummary? Member = null)
{
    public static AcceptInvitationResponse Failure { get; } = new(Accepted: false);
}

/// <summary>Success payload: user, hospital, actor, granted capabilities.</summary>
public sealed record MemberSummary(
    UserSummary User,
    HospitalSummary Hospital,
    ActorSummary Actor,
    IReadOnlyList<string> Capabilities);

public sealed record UserSummary(Guid Id, string Email);

public sealed record HospitalSummary(Guid Id, string Code, string Name);

public sealed record ActorSummary(string Id);

/// <summary>
/// Outcome of creating the invited identity user. <see cref="Duplicate"/>
/// signals an email already registered (the caller re-resolves and falls
/// back to the membership-only branch); non-empty <see cref="Errors"/>
/// means creation failed for a visible-request reason.
/// </summary>
public sealed record CreateUserResult(
    Guid? UserId,
    IReadOnlyList<string> Errors,
    bool Duplicate);
