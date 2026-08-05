using Cynara.Api.Tests.Documents.UnitTests.Fakes;
using Cynara.Api.Tests.Patients.UnitTests.Fakes;

using Cynara.Application.Modules.Patients;
using Cynara.Domain.Patients;

namespace Cynara.Api.Tests.Patients.UnitTests;

/// <summary>
/// Test harness that wires the patient service against the in-memory
/// fakes. Mirrors the shape of the document catalog harness so the
/// patient tests follow the same conventions: fixed clock, recording
/// unit-of-work, recording audit writer, and a resolved fake hospital
/// context.
/// </summary>
internal sealed class PatientsServiceHarness
{
    private PatientsServiceHarness(
        FakePatientRepository repository,
        RecordingUnitOfWork unitOfWork,
        RecordingAuditWriter auditWriter,
        FakeHospitalContext hospitalContext,
        FixedTimeProvider timeProvider)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
        AuditWriter = auditWriter;
        HospitalContext = hospitalContext;
        TimeProvider = timeProvider;
        Service = new PatientService(
            repository,
            unitOfWork,
            auditWriter,
            hospitalContext,
            timeProvider);
    }

    public FakePatientRepository Repository { get; }

    public RecordingUnitOfWork UnitOfWork { get; }

    public RecordingAuditWriter AuditWriter { get; }

    public FakeHospitalContext HospitalContext { get; }

    public FixedTimeProvider TimeProvider { get; }

    public PatientService Service { get; }

    public Guid HospitalId => HospitalContext.HospitalId;

    public static PatientsServiceHarness Create()
    {
        var hospitalId = Guid.NewGuid();
        return new PatientsServiceHarness(
            new FakePatientRepository(),
            new RecordingUnitOfWork(),
            new RecordingAuditWriter(),
            new FakeHospitalContext(hospitalId),
            new FixedTimeProvider(new DateTimeOffset(
                2026, 7, 27, 9, 0, 0, TimeSpan.Zero)));
    }

    public static Patient BuildPatient(
        Guid hospitalId,
        string mrn,
        string givenName,
        string familyName,
        DateOnly? birthDate = null,
        Sex? sex = null,
        uint rowVersion = 0,
        DateTimeOffset? deletedAt = null,
        string? nationalId = null)
    {
        DateTimeOffset now = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);
        return new Patient
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            Mrn = mrn,
            NormalizedMrn = mrn.Trim().ToUpperInvariant(),
            NationalId = nationalId,
            NormalizedNationalId = nationalId?.Trim().ToUpperInvariant(),
            GivenName = givenName,
            NormalizedGivenName = PatientWorkflowHelpers.NormalizeName(givenName),
            FamilyName = familyName,
            NormalizedFamilyName = PatientWorkflowHelpers.NormalizeName(familyName),
            BirthDate = birthDate ?? new DateOnly(1990, 1, 1),
            Sex = sex ?? Sex.Unknown,
            Status = PatientStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = deletedAt,
            RowVersion = rowVersion,
        };
    }

    public static CreatePatientRequest BuildCreateRequest(
        string mrn = "MRN-001",
        string givenName = "Ada",
        string familyName = "Lovelace",
        DateOnly? birthDate = null,
        string? sex = "female",
        string? nationalId = null)
    {
        return new CreatePatientRequest(
            Mrn: mrn,
            NationalId: nationalId,
            GivenName: givenName,
            FamilyName: familyName,
            BirthDate: birthDate ?? new DateOnly(1990, 1, 1),
            Sex: sex ?? "female");
    }

    public static UpdatePatientRequest BuildUpdateRequest(
        uint rowVersion,
        string givenName = "Ada",
        string familyName = "Lovelace",
        DateOnly? birthDate = null,
        string? sex = "female",
        string? nationalId = null)
    {
        return new UpdatePatientRequest(
            NationalId: nationalId,
            GivenName: givenName,
            FamilyName: familyName,
            BirthDate: birthDate ?? new DateOnly(1990, 1, 1),
            Sex: sex ?? "female",
            RowVersion: rowVersion);
    }
}
