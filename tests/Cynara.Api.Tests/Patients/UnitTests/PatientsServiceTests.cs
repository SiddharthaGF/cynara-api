using Cynara.Api.Tests.Documents.UnitTests.Fakes;

using Cynara.Application;
using Cynara.Application.Common;
using Cynara.Application.Modules.Patients;
using Cynara.Domain.Patients;

namespace Cynara.Api.Tests.Patients.UnitTests;

/// <summary>
/// Unit coverage for <see cref="PatientService"/>. The service is the
/// boundary that enforces the CYN-49 invariants: tenant scoping,
/// hospital-scoped MRN uniqueness, demographic validation, optimistic
/// concurrency, soft-delete behaviour, and audit emission. The
/// integration tests cover the happy path against Postgres; these tests
/// pin each branch that the integration suite does not exercise (cross-
/// tenant, soft-deleted, unknown id, validation, concurrency).
/// </summary>
public sealed class PatientsServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_RequiresResolvedTenant()
    {
        var harness = PatientsServiceHarness.Create();
        harness.HospitalContext.IsResolved = false;

        await Assert.ThrowsAsync<TenantContextException>(
            () => harness.Service.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_HidesSoftDeletedPatients()
    {
        var harness = PatientsServiceHarness.Create();
        Patient patient = PatientsServiceHarness.BuildPatient(
            harness.HospitalId,
            mrn: "MRN-001",
            givenName: "Ada",
            familyName: "Lovelace",
            deletedAt: Now);
        harness.Repository.Seed(patient);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.GetAsync(patient.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_HidesOtherTenantPatients()
    {
        var harness = PatientsServiceHarness.Create();
        var otherHospitalId = Guid.NewGuid();
        harness.Repository.Seed(
            PatientsServiceHarness.BuildPatient(
                harness.HospitalId,
                mrn: "MRN-OWN",
                givenName: "Ada",
                familyName: "Lovelace"),
            PatientsServiceHarness.BuildPatient(
                otherHospitalId,
                mrn: "MRN-OTHER",
                givenName: "Alan",
                familyName: "Turing"));

        IReadOnlyList<PatientDto> matches = await harness.Service
            .SearchAsync(
                new PatientSearchRequest(
                    Mrn: null,
                    NationalId: null,
                    GivenName: null,
                    FamilyName: null),
                CancellationToken.None)
            .ConfigureAwait(false);

        PatientDto single = Assert.Single(matches);
        Assert.Equal("MRN-OWN", single.Mrn);
    }

    [Fact]
    public async Task SearchAsync_NormalizesMrnFilter()
    {
        var harness = PatientsServiceHarness.Create();
        harness.Repository.Seed(
            PatientsServiceHarness.BuildPatient(
                harness.HospitalId,
                mrn: "MRN-001",
                givenName: "Ada",
                familyName: "Lovelace"),
            PatientsServiceHarness.BuildPatient(
                harness.HospitalId,
                mrn: "MRN-002",
                givenName: "Alan",
                familyName: "Turing"));

        IReadOnlyList<PatientDto> matches = await harness.Service
            .SearchAsync(
                new PatientSearchRequest(
                    Mrn: "  mrn-001  ",
                    NationalId: null,
                    GivenName: null,
                    FamilyName: null),
                CancellationToken.None)
            .ConfigureAwait(false);

        PatientDto single = Assert.Single(matches);
        Assert.Equal("MRN-001", single.Mrn);
    }

    [Fact]
    public async Task CreateAsync_NormalizesMrnToUppercase()
    {
        var harness = PatientsServiceHarness.Create();

        PatientDto created = await harness.Service
            .CreateAsync(
                PatientsServiceHarness.BuildCreateRequest(mrn: "  mrn-100  "),
                actorId: "actor-1",
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal("mrn-100", created.Mrn);
        Patient tracked = Assert.Single(harness.Repository.Added);
        Assert.Equal("MRN-100", tracked.NormalizedMrn);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);
        RecordingAuditWriter.AuditEntry audit = Assert.Single(harness.AuditWriter.Entries);
        Assert.Equal(AuditEntityTypes.Patient, audit.ResourceType);
        Assert.Equal("patient.created", audit.Action);
        Assert.Equal("actor-1", audit.ActorId);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateMrnWithinSameHospital()
    {
        var harness = PatientsServiceHarness.Create();
        harness.Repository.Seed(
            PatientsServiceHarness.BuildPatient(
                harness.HospitalId,
                mrn: "MRN-001",
                givenName: "Ada",
                familyName: "Lovelace"));

        ConflictException exception = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Service.CreateAsync(
                PatientsServiceHarness.BuildCreateRequest(mrn: "mrn-001"),
                actorId: null,
                CancellationToken.None));

        Assert.Contains("MRN-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_AllowsDuplicateMrnAcrossHospitals()
    {
        var harness = PatientsServiceHarness.Create();
        var otherHospitalId = Guid.NewGuid();
        harness.Repository.Seed(
            PatientsServiceHarness.BuildPatient(
                otherHospitalId,
                mrn: "MRN-001",
                givenName: "Ada",
                familyName: "Lovelace"));

        PatientDto created = await harness.Service
            .CreateAsync(
                PatientsServiceHarness.BuildCreateRequest(mrn: "MRN-001"),
                actorId: null,
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownSexValue()
    {
        var harness = PatientsServiceHarness.Create();
        CreatePatientRequest request = PatientsServiceHarness
            .BuildCreateRequest(mrn: "MRN-200") with
        { Sex = "not-a-real-sex", };

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.CreateAsync(
                request, actorId: null, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RejectsFutureBirthDate()
    {
        var harness = PatientsServiceHarness.Create();
        CreatePatientRequest request = PatientsServiceHarness
            .BuildCreateRequest(mrn: "MRN-201") with
        {
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)),
        };

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.CreateAsync(
                request, actorId: null, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RequiresConcurrencyMatch()
    {
        var harness = PatientsServiceHarness.Create();
        Patient patient = PatientsServiceHarness.BuildPatient(
            harness.HospitalId,
            mrn: "MRN-300",
            givenName: "Ada",
            familyName: "Lovelace",
            rowVersion: 5);
        harness.Repository.Seed(patient);

        UpdatePatientRequest request = PatientsServiceHarness
            .BuildUpdateRequest(rowVersion: 4);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => harness.Service.UpdateAsync(
                patient.Id, request, actorId: null, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsSoftDeletedPatient()
    {
        var harness = PatientsServiceHarness.Create();
        Patient patient = PatientsServiceHarness.BuildPatient(
            harness.HospitalId,
            mrn: "MRN-301",
            givenName: "Ada",
            familyName: "Lovelace",
            deletedAt: Now);
        harness.Repository.Seed(patient);

        UpdatePatientRequest request = PatientsServiceHarness
            .BuildUpdateRequest(rowVersion: patient.RowVersion);

        await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.UpdateAsync(
                patient.Id, request, actorId: null, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_BumpsRowVersionAndAudits()
    {
        var harness = PatientsServiceHarness.Create();
        Patient patient = PatientsServiceHarness.BuildPatient(
            harness.HospitalId,
            mrn: "MRN-302",
            givenName: "Ada",
            familyName: "Lovelace",
            rowVersion: 7);
        harness.Repository.Seed(patient);

        UpdatePatientRequest request = PatientsServiceHarness
            .BuildUpdateRequest(rowVersion: 7) with
        {
            FamilyName = "Byron",
        };

        PatientDto updated = await harness.Service
            .UpdateAsync(patient.Id, request, actorId: "actor-2", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal("Byron", updated.FamilyName);
        Assert.Equal(8U, updated.RowVersion);
        RecordingAuditWriter.AuditEntry audit = Assert.Single(harness.AuditWriter.Entries);
        Assert.Equal("patient.updated", audit.Action);
        Assert.Equal("actor-2", audit.ActorId);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task SoftDeleteAsync_HidesFromSearchByDefault()
    {
        var harness = PatientsServiceHarness.Create();
        Patient patient = PatientsServiceHarness.BuildPatient(
            harness.HospitalId,
            mrn: "MRN-400",
            givenName: "Ada",
            familyName: "Lovelace",
            rowVersion: 0);
        harness.Repository.Seed(patient);

        PatientDto deleted = await harness.Service
            .SoftDeleteAsync(
                patient.Id,
                new SoftDeletePatientRequest(0),
                actorId: null,
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.NotNull(deleted.DeletedAt);
        IReadOnlyList<PatientDto> hidden = await harness.Service
            .SearchAsync(
                new PatientSearchRequest(
                    Mrn: null,
                    NationalId: null,
                    GivenName: null,
                    FamilyName: null,
                    IncludeDeleted: false),
                CancellationToken.None)
            .ConfigureAwait(false);
        Assert.DoesNotContain(hidden, item => item.Id == patient.Id);

        IReadOnlyList<PatientDto> visible = await harness.Service
            .SearchAsync(
                new PatientSearchRequest(
                    Mrn: null,
                    NationalId: null,
                    GivenName: null,
                    FamilyName: null,
                    IncludeDeleted: true),
                CancellationToken.None)
            .ConfigureAwait(false);
        Assert.Contains(visible, item => item.Id == patient.Id);
    }

    [Fact]
    public async Task SoftDeleteAsync_RejectsDoubleDelete()
    {
        var harness = PatientsServiceHarness.Create();
        Patient patient = PatientsServiceHarness.BuildPatient(
            harness.HospitalId,
            mrn: "MRN-401",
            givenName: "Ada",
            familyName: "Lovelace",
            rowVersion: 0,
            deletedAt: Now);
        harness.Repository.Seed(patient);

        await Assert.ThrowsAsync<InvalidStateException>(
            () => harness.Service.SoftDeleteAsync(
                patient.Id,
                new SoftDeletePatientRequest(0),
                actorId: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task SoftDeleteAsync_RejectsStaleRowVersion()
    {
        var harness = PatientsServiceHarness.Create();
        Patient patient = PatientsServiceHarness.BuildPatient(
            harness.HospitalId,
            mrn: "MRN-402",
            givenName: "Ada",
            familyName: "Lovelace",
            rowVersion: 9);
        harness.Repository.Seed(patient);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => harness.Service.SoftDeleteAsync(
                patient.Id,
                new SoftDeletePatientRequest(8),
                actorId: null,
                CancellationToken.None));
    }
}
