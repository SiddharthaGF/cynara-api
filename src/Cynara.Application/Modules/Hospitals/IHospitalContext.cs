namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Per-request hospital workspace context. The infrastructure middleware
/// must populate this before any tenant-owned workflow runs; downstream
/// services read it via constructor injection.
/// </summary>
public interface IHospitalContext
{
    /// <summary>Resolved hospital identifier.</summary>
    public Guid HospitalId { get; }

    /// <summary>Stable business code supplied by the request.</summary>
    public string Code { get; }

    /// <summary>Display name copied from the database row.</summary>
    public string Name { get; }

    /// <summary>Indicates whether the context was resolved for this request.</summary>
    public bool IsResolved { get; }

    /// <summary>
    /// Throws <see cref="TenantContextException"/> when the context has not
    /// been resolved. Use this from workflows that must reject anonymous
    /// traffic.
    /// </summary>
    public void RequireResolved();
}
