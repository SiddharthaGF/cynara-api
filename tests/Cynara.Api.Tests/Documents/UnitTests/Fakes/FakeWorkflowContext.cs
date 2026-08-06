using Cynara.Application.Common;
using Cynara.Application.Modules.Hospitals;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IWorkflowContext"/> for unit tests that
/// composes the hospital context and clock fakes so the service constructor
/// surface stays within the workflow-context shape.
/// </summary>
public sealed class FakeWorkflowContext(
    IHospitalContext hospitalContext,
    TimeProvider timeProvider) : IWorkflowContext
{
    public void RequireResolved()
    {
        hospitalContext.RequireResolved();
    }

    public Guid HospitalId => hospitalContext.HospitalId;

    public DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow();
    }
}
