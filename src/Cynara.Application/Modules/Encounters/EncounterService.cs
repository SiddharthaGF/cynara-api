using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.ClinicalTaxonomy.Persistence;
using Cynara.Application.Modules.Encounters.Persistence;
using Cynara.Application.Modules.Patients.Persistence;
using Cynara.Application.Persistence;
using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Encounters;
using Cynara.Domain.Patients;

namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Default implementation of <see cref="IEncounterService"/>. All write
/// operations stamp ownership from the resolved hospital context, reject
/// cross-tenant or retired references, require valid lifecycle transitions,
/// and emit audit events that commit in the same unit-of-work transaction.
/// </summary>
public sealed class EncounterService(
    IEncounterRepository encounters,
    IPatientRepository patients,
    IClinicalTaxonomyRepository taxonomy,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IWorkflowContext context,
    ICapabilityGuard capabilityGuard) : IEncounterService
{
    /// <inheritdoc />
    public async Task<EncounterDto> CreateAsync(
        CreateEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.EncountersWrite, cancellationToken)
            .ConfigureAwait(false);

        EncounterType type = EncounterWorkflowHelpers.ParseType(request.Type);
        EncounterWorkflowHelpers.EnsureValidResponsibleProfessionalId(
            request.ResponsibleProfessionalId);

        Patient patient = await RequireActivePatientAsync(
                request.PatientId, cancellationToken)
            .ConfigureAwait(false);
        Facility facility = await RequireActiveFacilityAsync(
                request.FacilityId, cancellationToken)
            .ConfigureAwait(false);
        ClinicalArea clinicalArea = await RequireActiveClinicalAreaAsync(
                request.ClinicalAreaId, request.FacilityId, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = context.GetUtcNow();
        DateTimeOffset startedAt = request.StartedAt ?? now;
        Encounter encounter = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = context.HospitalId,
            PatientId = patient.Id,
            FacilityId = facility.Id,
            ClinicalAreaId = clinicalArea.Id,
            Type = type,
            ResponsibleProfessionalId =
                request.ResponsibleProfessionalId.Trim(),
            Status = EncounterStatus.Open,
            StartedAt = startedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.Encounter,
            encounter.Id,
            "encounter.created",
            actorId,
            now,
            new
            {
                patientId = encounter.PatientId,
                facilityId = encounter.FacilityId,
                clinicalAreaId = encounter.ClinicalAreaId,
                type = EncounterWorkflowHelpers.FormatType(encounter.Type),
                responsibleProfessionalId =
                    encounter.ResponsibleProfessionalId,
            });

        encounters.Add(encounter);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return EncounterMappers.ToDto(encounter);
    }

    /// <inheritdoc />
    public async Task<EncounterDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.EncountersRead, cancellationToken)
            .ConfigureAwait(false);
        Encounter encounter = await encounters
            .FindByIdAsync(
                context.HospitalId, id, track: false, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Encounter '{id}' was not found.");
        return EncounterMappers.ToDto(encounter);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EncounterDto>> ListAsync(
        EncounterListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.EncountersRead, cancellationToken)
            .ConfigureAwait(false);

        EncounterListCriteria criteria = new(
            request.PatientId,
            request.FacilityId,
            request.ClinicalAreaId,
            EncounterWorkflowHelpers.ParseStatusOrNull(request.Status));

        IReadOnlyList<Encounter> matches = await encounters
            .ListAsync(context.HospitalId, criteria, cancellationToken)
            .ConfigureAwait(false);
        return [.. matches.Select(EncounterMappers.ToDto)];
    }

    /// <inheritdoc />
    public async Task<EncounterDto> CompleteAsync(
        Guid id,
        TransitionEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        await capabilityGuard.RequireAsync(
            CapabilityCodes.EncountersWrite, cancellationToken)
            .ConfigureAwait(false);
        return await TransitionAsync(
            id,
            request,
            actorId,
            TerminalLifecycle.Trigger.Complete,
            "encounter.completed",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EncounterDto> CancelAsync(
        Guid id,
        TransitionEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        await capabilityGuard.RequireAsync(
            CapabilityCodes.EncountersWrite, cancellationToken)
            .ConfigureAwait(false);
        return await TransitionAsync(
            id,
            request,
            actorId,
            TerminalLifecycle.Trigger.Cancel,
            "encounter.canceled",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EncounterDto> EnterInErrorAsync(
        Guid id,
        TransitionEncounterRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        await capabilityGuard.RequireAsync(
            CapabilityCodes.EncountersWrite, cancellationToken)
            .ConfigureAwait(false);
        return await TransitionAsync(
            id,
            request,
            actorId,
            TerminalLifecycle.Trigger.EnterInError,
            "encounter.enteredInError",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<EncounterDto> TransitionAsync(
        Guid id,
        TransitionEncounterRequest request,
        string? actorId,
        TerminalLifecycle.Trigger trigger,
        string auditAction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.RequireResolved();

        Encounter encounter = await encounters
            .FindByIdAsync(
                context.HospitalId, id, track: true, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Encounter '{id}' was not found.");

        EncounterWorkflowHelpers.EnsureConcurrency(
            encounter.RowVersion, request.RowVersion);

        DateTimeOffset now = context.GetUtcNow();
        DateTimeOffset endedAt = request.EndedAt ?? now;
        EncounterWorkflowHelpers.EnsureEndedAtNotBeforeStart(
            encounter.StartedAt, endedAt);

        EncounterLifecycle.Fire(encounter, trigger);
        encounter.EndedAt = endedAt;
        encounter.UpdatedAt = now;
        encounter.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Encounter,
            encounter.Id,
            auditAction,
            actorId,
            now,
            new
            {
                status = EncounterWorkflowHelpers.FormatStatus(encounter.Status),
                endedAt = encounter.EndedAt,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return EncounterMappers.ToDto(encounter);
    }

    private async Task<Patient> RequireActivePatientAsync(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        Patient patient = await patients
            .FindByIdAsync(
                context.HospitalId,
                patientId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Patient '{patientId}' was not found.");

        if (patient.DeletedAt is not null)
        {
            throw new InvalidStateException(
                $"Patient '{patientId}' is deleted and cannot accept new "
                + "encounters.");
        }

        return patient;
    }

    private async Task<Facility> RequireActiveFacilityAsync(
        Guid facilityId,
        CancellationToken cancellationToken)
    {
        Facility facility = await taxonomy
            .FindFacilityByIdAsync(
                context.HospitalId,
                facilityId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Facility '{facilityId}' was not found.");

        if (facility.Status == ClinicalTaxonomyStatus.Retired)
        {
            throw new InvalidStateException(
                $"Facility '{facility.Code}' is retired; new encounters "
                + "cannot reference a retired facility.");
        }

        return facility;
    }

    private async Task<ClinicalArea> RequireActiveClinicalAreaAsync(
        Guid clinicalAreaId,
        Guid facilityId,
        CancellationToken cancellationToken)
    {
        ClinicalArea clinicalArea = await taxonomy
            .FindClinicalAreaByIdAsync(
                context.HospitalId,
                clinicalAreaId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical area '{clinicalAreaId}' was not found.");

        if (clinicalArea.FacilityId != facilityId)
        {
            throw new ValidationException(
                $"Clinical area '{clinicalArea.Code}' does not belong to "
                + $"facility '{facilityId}'.");
        }

        if (clinicalArea.Status == ClinicalTaxonomyStatus.Retired)
        {
            throw new InvalidStateException(
                $"Clinical area '{clinicalArea.Code}' is retired; new "
                + "encounters cannot reference a retired clinical area.");
        }

        return clinicalArea;
    }
}
