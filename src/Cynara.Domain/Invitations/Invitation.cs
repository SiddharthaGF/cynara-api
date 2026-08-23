using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Invitations;

/// <summary>
/// A single-use member invitation bound to exactly one hospital. Only the
/// latest issued link activates: resend regenerates the token hash and bumps
/// <see cref="LinkVersion"/>. The token never persists — only its SHA-256
/// hash — and carries no user, role, or capability data.
/// </summary>
/// <remarks>
/// Identity references are bare cross-track ids (no foreign key by design).
/// </remarks>
[NoResource]
public sealed class Invitation
{
    public Guid Id { get; set; }

    /// <summary>Invited email address; proves email possession on acceptance.</summary>
    public required string Email { get; set; }

    /// <summary>The single hospital this invitation grants membership in.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>
    /// Identity-track user linked when acceptance resolves an account;
    /// null until acceptance completes.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Opaque JSON snapshot of the predefined profile and capability set the
    /// administrator attached to this invitation (including whether the
    /// profile demands a professional identifier).
    /// </summary>
    public string? ProfileSnapshot { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    /// <summary>Increments on every resend; defines the only valid link.</summary>
    public int LinkVersion { get; set; } = 1;

    /// <summary>SHA-256 hex of the current link token; unique across invitations.</summary>
    public required string TokenHash { get; set; }

    /// <summary>When the current link was issued (reset by resend).</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Validity deadline of the current link (72 hours after issue).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public uint RowVersion { get; set; }
}
