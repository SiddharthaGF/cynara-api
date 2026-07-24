namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Default scoped instance of <see cref="IHospitalContext"/>. Infrastructure
/// middleware instantiates this with the resolved hospital row; uninitialized
/// state represents an anonymous request that must be rejected by tenant
/// workflows.
/// </summary>
public sealed class HospitalContext : IHospitalContext
{
    public Guid HospitalId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsResolved { get; private set; }

    public void RequireResolved()
    {
        if (!IsResolved)
        {
            throw new TenantContextException(
                "A hospital workspace context is required for this request.");
        }
    }

    /// <summary>Populates the context after the middleware resolves the hospital.</summary>
    public void SetWorkspace(Guid hospitalId, string code, string name)
    {
        HospitalId = hospitalId;
        Code = code;
        Name = name;
        IsResolved = true;
    }

    /// <summary>Resets the context (used by infrastructure tests).</summary>
    public void Reset()
    {
        HospitalId = default;
        Code = string.Empty;
        Name = string.Empty;
        IsResolved = false;
    }
}
