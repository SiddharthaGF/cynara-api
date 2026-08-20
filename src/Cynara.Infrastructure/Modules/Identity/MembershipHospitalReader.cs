using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Read-only implementation of <see cref="IHospitalMembershipReader"/> over
/// the identity and domain persistence contexts. Reads the user's
/// <see cref="Membership"/> rows without tracking, joins the hospital
/// workspaces by id, and projects each pair to its public code/name shape.
/// The identity track and the domain hospital table live on separate EF
/// contexts, so the join is performed in memory after two untracked queries;
/// no schema change is required. Memberships whose hospital row no longer
/// exists are skipped.
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
            .Where(item => item.UserId == userId)
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
}
