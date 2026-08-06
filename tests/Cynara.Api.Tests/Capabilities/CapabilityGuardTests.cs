using Cynara.Api.Tests.Capabilities.UnitTests;
using Cynara.Api.Tests.Documents.UnitTests.Fakes;

using Cynara.Application.Modules.Capabilities;
using Cynara.Domain.Capabilities;

namespace Cynara.Api.Tests.Capabilities;

public sealed class CapabilityGuardTests
{
    [Fact]
    public async Task RequireAsync_Passes_WhenCapabilityGranted()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsWrite,
            AssignedAt = DateTimeOffset.UtcNow,
        });
        var guard = new CapabilityGuard(
            new StubCurrentActor("registrar"),
            new EffectiveCapabilityResolver(
                new StubCurrentActor("registrar"),
                new FakeHospitalContext(hospitalId),
                repository));

        await guard.RequireAsync(
            CapabilityCodes.PatientsWrite,
            CancellationToken.None);
    }

    [Fact]
    public async Task RequireAsync_Throws_WhenCapabilityNotGranted()
    {
        var hospitalId = Guid.NewGuid();
        var guard = new CapabilityGuard(
            new StubCurrentActor("registrar"),
            new EffectiveCapabilityResolver(
                new StubCurrentActor("registrar"),
                new FakeHospitalContext(hospitalId),
                new FakeCapabilityAssignmentRepository()));

        CapabilityForbiddenException exception = await Assert.ThrowsAsync<
            CapabilityForbiddenException>(
            () => guard.RequireAsync(
                CapabilityCodes.PatientsWrite,
                CancellationToken.None));

        Assert.Equal(CapabilityCodes.PatientsWrite, exception.Capability);
        Assert.Equal("registrar", exception.ActorId);
    }

    [Fact]
    public async Task RequireAsync_Throws_WhenNoActorIdentity()
    {
        var hospitalId = Guid.NewGuid();
        var guard = new CapabilityGuard(
            new StubCurrentActor(actorId: null),
            new EffectiveCapabilityResolver(
                new StubCurrentActor(actorId: null),
                new FakeHospitalContext(hospitalId),
                new FakeCapabilityAssignmentRepository()));

        CapabilityForbiddenException exception = await Assert.ThrowsAsync<
            CapabilityForbiddenException>(
            () => guard.RequireAsync(
                CapabilityCodes.PatientsWrite,
                CancellationToken.None));

        Assert.Null(exception.ActorId);
    }

    [Fact]
    public async Task RequireAsync_Throws_ForNullCapability()
    {
        var guard = new CapabilityGuard(
            new StubCurrentActor("registrar"),
            new EffectiveCapabilityResolver(
                new StubCurrentActor("registrar"),
                new FakeHospitalContext(Guid.NewGuid()),
                new FakeCapabilityAssignmentRepository()));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => guard.RequireAsync(
                capability: null!,
                CancellationToken.None));
    }
}
