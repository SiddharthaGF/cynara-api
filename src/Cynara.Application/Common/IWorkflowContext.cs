using Cynara.Application.Modules.Hospitals;

namespace Cynara.Application.Common;

/// <summary>
/// Ambient workflow context grouping the tenant scope and the clock.
/// Workflows with many collaborators use this port instead of taking
/// <see cref="IHospitalContext"/> and <see cref="TimeProvider"/> separately;
/// it owns no state.
/// </summary>
public interface IWorkflowContext
{
    /// <summary>Identifier of the resolved hospital workspace.</summary>
    public Guid HospitalId { get; }

    /// <summary>
    /// Throws <see cref="TenantContextException"/> when no hospital workspace
    /// has been resolved for the current request.
    /// </summary>
    public void RequireResolved();

    /// <summary>Returns the current UTC timestamp from the workflow clock.</summary>
    public DateTimeOffset GetUtcNow();
}
