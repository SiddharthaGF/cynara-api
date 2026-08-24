using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Patients.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Patients;

namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Default implementation of <see cref="IPatientService"/>. All writes stamp
/// ownership from the resolved hospital context, enforce hospital-scoped
/// MRN uniqueness, and emit audit events in the same unit-of-work
/// transaction; soft-deleted patients stay hidden but resolvable for audit.
/// </summary>
public sealed class PatientService(
    IPatientRepository repository,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    ICapabilityGuard capabilityGuard) : IPatientService
{
    /// <inheritdoc />
    public async Task<PatientDto> CreateAsync(
        CreatePatientRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PatientsWrite, cancellationToken)
            .ConfigureAwait(false);

        PatientWorkflowHelpers.EnsureValidMrn(request.Mrn);
        PatientWorkflowHelpers.EnsureValidNationalId(request.NationalId);
        PatientWorkflowHelpers.EnsureValidName(request.GivenName, "given name");
        PatientWorkflowHelpers.EnsureValidName(request.FamilyName, "family name");
        PatientWorkflowHelpers.EnsureValidBirthDate(request.BirthDate);
        Sex sex = PatientWorkflowHelpers.ParseSex(request.Sex);
        BloodType bloodType = PatientWorkflowHelpers.ParseBloodType(request.BloodType);

        string normalizedMrn = PatientWorkflowHelpers.NormalizeMrn(request.Mrn);
        string? normalizedNationalId =
            PatientWorkflowHelpers.NormalizeNationalId(request.NationalId);

        Patient? existing = await repository
            .FindByNormalizedMrnAsync(
                hospitalContext.HospitalId,
                normalizedMrn,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && existing.DeletedAt is null)
        {
            throw new ConflictException(
                $"Patient with MRN '{normalizedMrn}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Patient patient = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            Mrn = request.Mrn.Trim(),
            NormalizedMrn = normalizedMrn,
            NationalId = request.NationalId?.Trim(),
            NormalizedNationalId = normalizedNationalId,
            GivenName = request.GivenName.Trim(),
            NormalizedGivenName =
                PatientWorkflowHelpers.NormalizeName(request.GivenName),
            FamilyName = request.FamilyName.Trim(),
            NormalizedFamilyName =
                PatientWorkflowHelpers.NormalizeName(request.FamilyName),
            BirthDate = request.BirthDate,
            Sex = sex,
            BloodType = bloodType,
            Status = PatientStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.Patient,
            patient.Id,
            "patient.created",
            actorId,
            now,
            new
            {
                mrn = patient.NormalizedMrn,
                nationalId = patient.NormalizedNationalId,
                givenName = patient.GivenName,
                familyName = patient.FamilyName,
            });

        repository.Add(patient);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return PatientMappers.ToDto(patient);
    }

    /// <inheritdoc />
    public async Task<PatientDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PatientsRead, cancellationToken)
            .ConfigureAwait(false);
        Patient patient = await repository
            .FindByIdAsync(hospitalContext.HospitalId, id, track: false, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Patient '{id}' was not found.");

        if (patient.DeletedAt is not null)
        {
            throw new NotFoundException($"Patient '{id}' was not found.");
        }

        return PatientMappers.ToDto(patient);
    }

    /// <inheritdoc />
    public async Task<PatientListResponse> SearchAsync(
        PatientSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PatientsRead, cancellationToken)
            .ConfigureAwait(false);

        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize < 1
            ? PatientFieldLimits.DefaultPageSize
            : Math.Min(request.PageSize, PatientFieldLimits.MaxPageSize);

        IReadOnlyList<string> nameTokens = PatientWorkflowHelpers.TokenizeNameFilter(
            request.GivenName,
            request.FamilyName);
        PatientSearchCriteria criteria = new(
            PatientWorkflowHelpers.NormalizeMrnOrNull(request.Mrn),
            PatientWorkflowHelpers.NormalizeNationalId(request.NationalId),
            nameTokens,
            request.IncludeDeleted);

        PatientSearchPage pageResult = await repository
            .SearchAsync(
                hospitalContext.HospitalId,
                criteria,
                page,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);

        return new PatientListResponse(
            [.. pageResult.Items.Select(PatientMappers.ToDto)],
            page,
            pageSize,
            pageResult.TotalCount);
    }

    /// <inheritdoc />
    public async Task<PatientDto> UpdateAsync(
        Guid id,
        UpdatePatientRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PatientsWrite, cancellationToken)
            .ConfigureAwait(false);

        PatientWorkflowHelpers.EnsureValidNationalId(request.NationalId);
        PatientWorkflowHelpers.EnsureValidName(request.GivenName, "given name");
        PatientWorkflowHelpers.EnsureValidName(request.FamilyName, "family name");
        PatientWorkflowHelpers.EnsureValidBirthDate(request.BirthDate);
        Sex sex = PatientWorkflowHelpers.ParseSex(request.Sex);
        BloodType bloodType = PatientWorkflowHelpers.ParseBloodType(request.BloodType);

        Patient patient = await repository
            .FindByIdAsync(hospitalContext.HospitalId, id, track: true, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Patient '{id}' was not found.");
        PatientWorkflowHelpers.EnsureNotDeleted(patient);
        PatientWorkflowHelpers.EnsureConcurrency(
            patient.RowVersion, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        patient.NationalId = request.NationalId?.Trim();
        patient.NormalizedNationalId =
            PatientWorkflowHelpers.NormalizeNationalId(request.NationalId);
        patient.GivenName = request.GivenName.Trim();
        patient.NormalizedGivenName =
            PatientWorkflowHelpers.NormalizeName(request.GivenName);
        patient.FamilyName = request.FamilyName.Trim();
        patient.NormalizedFamilyName =
            PatientWorkflowHelpers.NormalizeName(request.FamilyName);
        patient.BirthDate = request.BirthDate;
        patient.Sex = sex;
        patient.BloodType = bloodType;
        patient.UpdatedAt = now;
        patient.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Patient,
            patient.Id,
            "patient.updated",
            actorId,
            now,
            new
            {
                mrn = patient.NormalizedMrn,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return PatientMappers.ToDto(patient);
    }

    /// <inheritdoc />
    public async Task<PatientDto> SoftDeleteAsync(
        Guid id,
        SoftDeletePatientRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.PatientsWrite, cancellationToken)
            .ConfigureAwait(false);

        Patient patient = await repository
            .FindByIdAsync(hospitalContext.HospitalId, id, track: true, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Patient '{id}' was not found.");
        PatientWorkflowHelpers.EnsureConcurrency(
            patient.RowVersion, request.RowVersion);
        if (patient.DeletedAt is not null)
        {
            throw new InvalidStateException(
                $"Patient '{id}' is already deleted.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        patient.DeletedAt = now;
        patient.UpdatedAt = now;
        patient.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Patient,
            patient.Id,
            "patient.deleted",
            actorId,
            now,
            new
            {
                mrn = patient.NormalizedMrn,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return PatientMappers.ToDto(patient);
    }
}
