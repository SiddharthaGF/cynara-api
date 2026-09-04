using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Memberships;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Read-only implementation of <see cref="IHospitalMembershipReader"/>
/// joining identity membership rows to domain hospitals in memory (they
/// live on separate contexts); untracked reads, missing hospitals skipped.
/// </summary>
public sealed class MembershipHospitalReader(
    CynaraIdentityDbContext identity,
    CynaraDbContext domain) : IHospitalMembershipReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<HospitalMembershipDto>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        List<Membership> memberships = await identity.Memberships
            .AsNoTracking()
            .Where(item => item.UserId == userId
                && item.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (memberships.Count == 0)
        {
            return [];
        }

        Guid[] hospitalIds = [.. memberships
            .Select(item => item.HospitalId)
            .Distinct()];

        List<Domain.Hospitals.Hospital> hospitals = await domain.Hospitals
            .AsNoTracking()
            .Where(hospital => hospitalIds.Contains(hospital.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. memberships
            .Join(
                hospitals,
                membership => membership.HospitalId,
                hospital => hospital.Id,
                static (_, hospital) =>
                    new HospitalMembershipDto(hospital.Code, hospital.Name))];
    }

    /// <inheritdoc />
    public Task<string?> FindActorIdAsync(
        Guid userId,
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        return identity.Memberships
            .AsNoTracking()
            .Where(item => item.UserId == userId
                && item.HospitalId == hospitalId
                && item.Status == MembershipStatus.Active)
            .Select(item => (string?)item.ActorId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
