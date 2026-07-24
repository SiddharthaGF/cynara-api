using Cynara.Domain.Hospitals;

namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Persistence port for the hospital workspace aggregate. Reads return
/// null when the hospital is unknown; writes must keep the surrogate
/// identifier and update the optimistic concurrency token.
/// </summary>
public interface IHospitalRepository
{
    public Task<Hospital?> FindByIdAsync(
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    public Task<Hospital?> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<Hospital>> ListAsync(CancellationToken cancellationToken);

    public void Add(Hospital hospital);

    public void Update(Hospital hospital);
}
