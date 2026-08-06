using Cynara.Api.Tests.Capabilities.UnitTests;
using Cynara.Api.Tests.Documents.UnitTests.Fakes;

using Cynara.Application.Modules.Capabilities;
using Cynara.Domain.Capabilities;

namespace Cynara.Api.Tests.Capabilities;

public sealed class EffectiveCapabilityResolverTests
{
    private static EffectiveCapabilityResolver BuildResolver(
        Guid hospitalId,
        string? actorId,
        FakeCapabilityAssignmentRepository repository,
        bool isResolved = true)
    {
        return new EffectiveCapabilityResolver(
            new StubCurrentActor(actorId),
            new FakeHospitalContext(hospitalId) { IsResolved = isResolved },
            repository);
    }

    private static CapabilityAssignment Assignment(
        Guid hospitalId,
        string actorId,
        string capability)
    {
        return new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = actorId,
            Capability = capability,
            AssignedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task ResolveAsync_ReturnsEmptySet_WhenNoActorIdentity()
    {
        var repository = new FakeCapabilityAssignmentRepository();
        EffectiveCapabilityResolver resolver = BuildResolver(
            Guid.NewGuid(),
            actorId: null,
            repository);

        IReadOnlySet<string> effective = await resolver
            .ResolveAsync(CancellationToken.None);

        Assert.Empty(effective);
        Assert.Equal(0, repository.ListCapabilityCodesCalls);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsEmptySet_WhenHospitalUnresolved()
    {
        var repository = new FakeCapabilityAssignmentRepository();
        EffectiveCapabilityResolver resolver = BuildResolver(
            Guid.NewGuid(),
            actorId: "registrar",
            repository,
            isResolved: false);

        IReadOnlySet<string> effective = await resolver
            .ResolveAsync(CancellationToken.None);

        Assert.Empty(effective);
        Assert.Equal(0, repository.ListCapabilityCodesCalls);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsAssignedCodes_ForResolvedActor()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(Assignment(
            hospitalId,
            "registrar",
            CapabilityCodes.PatientsRead));
        repository.Seed(Assignment(
            hospitalId,
            "registrar",
            CapabilityCodes.PatientsWrite));
        EffectiveCapabilityResolver resolver = BuildResolver(
            hospitalId,
            "registrar",
            repository);

        IReadOnlySet<string> effective = await resolver
            .ResolveAsync(CancellationToken.None);

        Assert.Equal(2, effective.Count);
        Assert.Contains(CapabilityCodes.PatientsRead, effective);
        Assert.Contains(CapabilityCodes.PatientsWrite, effective);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresAssignments_FromOtherHospital()
    {
        var hospitalId = Guid.NewGuid();
        var otherHospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(Assignment(
            otherHospitalId,
            "registrar",
            CapabilityCodes.PatientsWrite));
        EffectiveCapabilityResolver resolver = BuildResolver(
            hospitalId,
            "registrar",
            repository);

        IReadOnlySet<string> effective = await resolver
            .ResolveAsync(CancellationToken.None);

        Assert.Empty(effective);
    }

    [Fact]
    public async Task ResolveAsync_Caches_AfterFirstResolution()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        EffectiveCapabilityResolver resolver = BuildResolver(
            hospitalId,
            "registrar",
            repository);

        _ = await resolver.ResolveAsync(CancellationToken.None);
        _ = await resolver.ResolveAsync(CancellationToken.None);
        _ = await resolver.HasCapabilityAsync(
            CapabilityCodes.PatientsRead,
            CancellationToken.None);

        Assert.Equal(1, repository.ListCapabilityCodesCalls);
    }

    [Fact]
    public async Task HasCapabilityAsync_ReturnsFalse_ForUnassignedCapability()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(Assignment(
            hospitalId,
            "registrar",
            CapabilityCodes.PatientsRead));
        EffectiveCapabilityResolver resolver = BuildResolver(
            hospitalId,
            "registrar",
            repository);

        bool granted = await resolver.HasCapabilityAsync(
            CapabilityCodes.EncountersRead,
            CancellationToken.None);

        Assert.False(granted);
    }

    [Fact]
    public async Task HasCapabilityAsync_ReturnsTrue_ForAssignedCapability()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(Assignment(
            hospitalId,
            "registrar",
            CapabilityCodes.PatientsRead));
        EffectiveCapabilityResolver resolver = BuildResolver(
            hospitalId,
            "registrar",
            repository);

        bool granted = await resolver.HasCapabilityAsync(
            CapabilityCodes.PatientsRead,
            CancellationToken.None);

        Assert.True(granted);
    }
}
