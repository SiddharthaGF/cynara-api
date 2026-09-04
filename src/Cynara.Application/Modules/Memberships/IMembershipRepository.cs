namespace Cynara.Application.Modules.Memberships;

/// <summary>
/// Identity-track persistence port for membership administration.
/// Implementations track or read without tracking as requested but NEVER
/// commit: workflows own the single <c>SaveChangesAsync</c> boundary that
/// also commits staged audit events. Slice 2 covers add/update/list;
/// slice 3 adds revoke/reactivate below.
/// </summary>
public interface IMembershipRepository
{
    public Task<MembershipRow?> FindByIdAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    /// <summary>Hospital history newest-first, status exposed.</summary>
    public Task<IReadOnlyList<MembershipRow>> ListByHospitalAsync(
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task<bool> IsActorIdActiveAsync(
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken);

    public Task<bool> HasActiveAsync(
        Guid userId,
        Guid hospitalId,
        CancellationToken cancellationToken);

    public Task<bool> UserExistsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new Active row; the caller commits.
    /// </summary>
    public MembershipRow Add(
        Guid userId,
        Guid hospitalId,
        string actorId,
        DateTimeOffset now);

    /// <summary>
    /// Revokes the tracked current row (stamping <c>RevokedAt</c>) and
    /// stages a new Active row with <paramref name="newActorId"/>; the
    /// caller commits both in one transaction.
    /// </summary>
    public Task<MembershipRow> ReplaceActorAsync(
        Guid currentId,
        string newActorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Flips the tracked current row to Revoked (stamping
    /// <c>RevokedAt</c>, bumping <c>RowVersion</c>); the caller commits.
    /// </summary>
    public Task<MembershipRow> RevokeAsync(
        Guid currentId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new Active row for the revoked row's (user, hospital)
    /// with <paramref name="actorId"/>; the revoked row stays history
    /// and the caller commits both in one transaction.
    /// </summary>
    public Task<MembershipRow> ReactivateAsync(
        Guid revokedId,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
