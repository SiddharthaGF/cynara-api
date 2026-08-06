using Cynara.Application.Modules.Hospitals;

namespace Cynara.Application.Common;

/// <summary>
/// Default implementation of <see cref="IWorkflowContext"/>. Delegates tenant
/// resolution to the scoped <see cref="IHospitalContext"/> and the clock to
/// the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class WorkflowContext(
    IHospitalContext hospitalContext,
    TimeProvider timeProvider) : IWorkflowContext
{
    public Guid HospitalId => hospitalContext.HospitalId;

    public void RequireResolved()
    {
        hospitalContext.RequireResolved();
    }

    public DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow();
    }
}
