using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Modules.Hospitals;

public sealed class HospitalRepository(CynaraDbContext dbContext) : IHospitalRepository
{
    public Task<Hospital?> FindByIdAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<Hospital> query = track
            ? dbContext.Hospitals
            : dbContext.Hospitals.AsNoTracking();
        return query.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);
    }

    public Task<Hospital?> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        return dbContext.Hospitals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == code,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Hospital>> ListAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.Hospitals
            .AsNoTracking()
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Hospital hospital)
    {
        _ = dbContext.Hospitals.Add(hospital);
    }

    public void Update(Hospital hospital)
    {
        _ = dbContext.Hospitals.Update(hospital);
    }
}
