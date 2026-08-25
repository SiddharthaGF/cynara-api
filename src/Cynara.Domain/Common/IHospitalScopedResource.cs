namespace Cynara.Domain.Common;

/// <summary>
/// Marks entities that are owned by a hospital workspace. The JSON:API
/// repository layer relies on this contract to push the tenant predicate
/// into SQL before pagination, sorting, and filters are applied.
/// </summary>
public interface IHospitalScopedResource
{
    /// <summary>Owning hospital workspace identifier.</summary>
    public Guid HospitalId { get; }
}
