using Cynara.Api.Tests.Capabilities.UnitTests;
using Cynara.Api.Tests.Documents.UnitTests.Fakes;
using Cynara.Application;
using Cynara.Application.Modules.Capabilities;
using Cynara.Domain.Capabilities;

namespace Cynara.Api.Tests.Capabilities;

public sealed class CapabilityAssignmentServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    private static CapabilityAssignmentService BuildService(
        Guid hospitalId,
        FakeCapabilityAssignmentRepository? repository = null,
        RecordingAuditWriter? auditWriter = null,
        RecordingUnitOfWork? unitOfWork = null,
        bool isResolved = true)
    {
        return new CapabilityAssignmentService(
            repository ?? new FakeCapabilityAssignmentRepository(),
            unitOfWork ?? new RecordingUnitOfWork(),
            auditWriter ?? new RecordingAuditWriter(),
            new FakeHospitalContext(hospitalId) { IsResolved = isResolved },
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task GrantAsync_AddsAssignment_AndAudits()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        var auditWriter = new RecordingAuditWriter();
        CapabilityAssignmentService service = BuildService(
            hospitalId,
            repository,
            auditWriter);

        CapabilityAssignmentDto dto = await service.GrantAsync(
            new GrantCapabilityRequest("registrar", CapabilityCodes.PatientsWrite),
            assignedBy: "admin",
            CancellationToken.None);

        Assert.Equal("registrar", dto.ActorId);
        Assert.Equal(CapabilityCodes.PatientsWrite, dto.Capability);
        Assert.Equal("admin", dto.AssignedBy);
        Assert.Equal(Now, dto.AssignedAt);

        RecordingAuditWriter.AuditEntry entry = Assert.Single(
            auditWriter.Entries);
        Assert.Equal("capability.assigned", entry.Action);
        Assert.Equal("capability-assignment", entry.ResourceType);
        Assert.Equal("admin", entry.ActorId);
        Assert.Equal(Now, entry.OccurredAt);
    }

    [Fact]
    public async Task GrantAsync_ThrowsConflict_WhenAlreadyGranted()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsWrite,
            AssignedAt = Now,
        });
        CapabilityAssignmentService service = BuildService(
            hospitalId,
            repository);

        _ = await Assert.ThrowsAsync<ConflictException>(
            () => service.GrantAsync(
                new GrantCapabilityRequest(
                    "registrar",
                    CapabilityCodes.PatientsWrite),
                assignedBy: "admin",
                CancellationToken.None));
    }

    [Fact]
    public async Task GrantAsync_RejectsUnknownCapability()
    {
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid());

        ValidationException exception = await Assert.ThrowsAsync<
            ValidationException>(
            () => service.GrantAsync(
                new GrantCapabilityRequest("registrar", "patients.magical"),
                assignedBy: "admin",
                CancellationToken.None));

        Assert.Contains(
            "catalog",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrantAsync_RejectsBlankActorId()
    {
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid());

        ValidationException exception = await Assert.ThrowsAsync<
            ValidationException>(
            () => service.GrantAsync(
                new GrantCapabilityRequest(
                    "   ",
                    CapabilityCodes.PatientsWrite),
                assignedBy: "admin",
                CancellationToken.None));

        Assert.Contains(
            "actorId",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrantAsync_RequiresResolvedHospital()
    {
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid(),
            isResolved: false);

        _ = await Assert.ThrowsAsync<TenantContextException>(
            () => service.GrantAsync(
                new GrantCapabilityRequest(
                    "registrar",
                    CapabilityCodes.PatientsWrite),
                assignedBy: "admin",
                CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_RemovesAssignment_AndAudits()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        var assignment = new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsWrite,
            AssignedAt = Now,
            AssignedBy = "admin",
        };
        repository.Seed(assignment);
        var auditWriter = new RecordingAuditWriter();
        CapabilityAssignmentService service = BuildService(
            hospitalId,
            repository,
            auditWriter);

        await service.RevokeAsync(
            "registrar",
            CapabilityCodes.PatientsWrite,
            revokedBy: "admin",
            scope: CapabilityScopes.Hospital,
            CancellationToken.None);

        CapabilityAssignment? remaining = await repository.FindAsync(
            hospitalId,
            "registrar",
            CapabilityCodes.PatientsWrite,
            CapabilityScopes.Hospital,
            track: false,
            CancellationToken.None);
        Assert.Null(remaining);

        RecordingAuditWriter.AuditEntry entry = Assert.Single(
            auditWriter.Entries);
        Assert.Equal("capability.revoked", entry.Action);
        Assert.Equal(assignment.Id, entry.ResourceId);
    }

    [Fact]
    public async Task RevokeAsync_ThrowsNotFound_WhenNotHeld()
    {
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid());

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.RevokeAsync(
                "registrar",
                CapabilityCodes.PatientsWrite,
                revokedBy: "admin",
                scope: CapabilityScopes.Hospital,
                CancellationToken.None));
    }

    [Fact]
    public async Task GrantAsync_PlatformScope_ConflictsGlobally()
    {
        var issuingHospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = Guid.NewGuid(),
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsWrite,
            Scope = CapabilityScopes.Platform,
            AssignedAt = Now,
        });
        CapabilityAssignmentService service = BuildService(
            issuingHospitalId,
            repository);

        _ = await Assert.ThrowsAsync<ConflictException>(
            () => service.GrantAsync(
                new GrantCapabilityRequest(
                    "registrar",
                    CapabilityCodes.PatientsWrite,
                    CapabilityScopes.Platform),
                assignedBy: "admin",
                CancellationToken.None));
    }

    [Fact]
    public async Task GrantAsync_PlatformScope_Succeeds_WhenOnlyHospitalRowHeld()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsWrite,
            AssignedAt = Now,
        });
        CapabilityAssignmentService service = BuildService(
            hospitalId,
            repository);

        _ = await service.GrantAsync(
            new GrantCapabilityRequest(
                "registrar",
                CapabilityCodes.PatientsWrite,
                CapabilityScopes.Platform),
            assignedBy: "admin",
            CancellationToken.None);

        CapabilityAssignment? created = await repository.FindAsync(
            hospitalId,
            "registrar",
            CapabilityCodes.PatientsWrite,
            CapabilityScopes.Platform,
            track: false,
            CancellationToken.None);
        Assert.NotNull(created);
        Assert.Equal(hospitalId, created.HospitalId);
        Assert.Equal(2, repository.Assignments.Count);
    }

    [Fact]
    public async Task GrantAsync_HospitalScope_Succeeds_WhenOnlyPlatformRowHeld()
    {
        var hospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsWrite,
            Scope = CapabilityScopes.Platform,
            AssignedAt = Now,
        });
        CapabilityAssignmentService service = BuildService(
            hospitalId,
            repository);

        _ = await service.GrantAsync(
            new GrantCapabilityRequest(
                "registrar",
                CapabilityCodes.PatientsWrite),
            assignedBy: "admin",
            CancellationToken.None);

        Assert.Equal(2, repository.Assignments.Count);
    }

    [Fact]
    public async Task GrantAsync_RejectsUnknownScope()
    {
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid());

        ValidationException exception = await Assert.ThrowsAsync<
            ValidationException>(
            () => service.GrantAsync(
                new GrantCapabilityRequest(
                    "registrar",
                    CapabilityCodes.PatientsWrite,
                    "galactic"),
                assignedBy: "admin",
                CancellationToken.None));

        Assert.Contains(
            "not recognized",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeAsync_PlatformScope_RemovesGlobalRow()
    {
        var issuingHospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        var assignment = new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = issuingHospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.UsersRead,
            Scope = CapabilityScopes.Platform,
            AssignedAt = Now,
            AssignedBy = "admin",
        };
        repository.Seed(assignment);
        var auditWriter = new RecordingAuditWriter();
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid(),
            repository,
            auditWriter);

        await service.RevokeAsync(
            "registrar",
            CapabilityCodes.UsersRead,
            revokedBy: "admin",
            scope: CapabilityScopes.Platform,
            CancellationToken.None);

        CapabilityAssignment? remaining = await repository.FindAsync(
            issuingHospitalId,
            "registrar",
            CapabilityCodes.UsersRead,
            CapabilityScopes.Platform,
            track: false,
            CancellationToken.None);
        Assert.Null(remaining);
        RecordingAuditWriter.AuditEntry entry = Assert.Single(
            auditWriter.Entries);
        Assert.Equal("capability.revoked", entry.Action);
        Assert.Equal(assignment.Id, entry.ResourceId);
    }

    [Fact]
    public async Task RevokeAsync_HospitalScope_DoesNotMatchPlatformRow()
    {
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = Guid.NewGuid(),
            ActorId = "registrar",
            Capability = CapabilityCodes.UsersRead,
            Scope = CapabilityScopes.Platform,
            AssignedAt = Now,
        });
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid(),
            repository);

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.RevokeAsync(
                "registrar",
                CapabilityCodes.UsersRead,
                revokedBy: "admin",
                scope: null,
                CancellationToken.None));

        Assert.Single(repository.Assignments);
    }

    [Fact]
    public async Task RevokeAsync_RejectsUnknownScope()
    {
        CapabilityAssignmentService service = BuildService(
            Guid.NewGuid());

        ValidationException exception = await Assert.ThrowsAsync<
            ValidationException>(
            () => service.RevokeAsync(
                "registrar",
                CapabilityCodes.PatientsWrite,
                revokedBy: "admin",
                scope: "galactic",
                CancellationToken.None));

        Assert.Contains(
            "not recognized",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_ReturnsHospitalScopedAssignments()
    {
        var hospitalId = Guid.NewGuid();
        var otherHospitalId = Guid.NewGuid();
        var repository = new FakeCapabilityAssignmentRepository();
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsRead,
            AssignedAt = Now,
        });
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = "doctor",
            Capability = CapabilityCodes.FormResponsesRead,
            AssignedAt = Now.AddMinutes(1),
        });
        repository.Seed(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = otherHospitalId,
            ActorId = "registrar",
            Capability = CapabilityCodes.PatientsWrite,
            AssignedAt = Now,
        });
        CapabilityAssignmentService service = BuildService(
            hospitalId,
            repository);

        CapabilityAssignmentListResponse response = await service
            .ListAsync(CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(
            "doctor",
            response.Items[0].ActorId);
        Assert.Equal(
            "registrar",
            response.Items[1].ActorId);
    }
}
