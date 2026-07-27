using Cynara.Application;
using Cynara.Application.Modules.Hospitals;

namespace Cynara.Api.Tests.Documents.UnitTests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IHospitalContext"/> for unit tests. Defaults
/// to a resolved context bound to a fixed hospital id so the workflow can
/// skip the tenant boundary.
/// </summary>
public sealed class FakeHospitalContext : IHospitalContext
{
    public FakeHospitalContext(Guid hospitalId, string code = "test")
    {
        HospitalId = hospitalId;
        Code = code;
        Name = "Test workspace";
    }

    public Guid HospitalId { get; }

    public string Code { get; }

    public string Name { get; }

    public bool IsResolved { get; set; } = true;

    public void RequireResolved()
    {
        if (!IsResolved)
        {
            throw new TenantContextException("Hospital context is not resolved.");
        }
    }
}
