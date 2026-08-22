using Cynara.Application.Modules.Encounters;
using Cynara.Application.Modules.Encounters.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Patients.Persistence;
using Cynara.Domain.Encounters;
using Cynara.Domain.Patients;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Resolves the patient/encounter binding a pipeline is started against.
/// The resolved hospital must contain the subject record and it must be in
/// an active state; otherwise the same exceptions the pipeline API surfaced
/// before the split are rethrown with identical messages.
/// </summary>
public sealed class PipelineSubjectResolver(
    IEncounterRepository encounters,
    IPatientRepository patients,
    IHospitalContext hospitalContext)
{
    /// <summary>Resolves an active subject for a new pipeline.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the subject type has no registered resolver.
    /// </exception>
    public async Task<PipelineSubjectBinding> ResolveAsync(
        PipelineSubjectType subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        return subjectType switch
        {
            PipelineSubjectType.Encounter => await ResolveEncounterAsync(
                    subjectId, cancellationToken)
                .ConfigureAwait(false),
            PipelineSubjectType.Patient => await ResolvePatientAsync(
                    subjectId, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Unknown pipeline subject type '{subjectType}'."),
        };
    }

    /// <summary>
    /// Verifies the subject record exists in the resolved hospital without
    /// requiring an active state, so historical journeys keep rendering for
    /// terminal encounters and soft-deleted patients.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the subject type has no registered existence check.
    /// </exception>
    public async Task EnsureSubjectExistsAsync(
        PipelineSubjectType subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        switch (subjectType)
        {
            case PipelineSubjectType.Encounter:
                await EnsureEncounterExistsAsync(subjectId, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineSubjectType.Patient:
                await EnsurePatientExistsAsync(subjectId, cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown pipeline subject type '{subjectType}'.");
        }
    }

    private async Task EnsureEncounterExistsAsync(
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        _ = await encounters
            .FindByIdAsync(
                hospitalContext.HospitalId,
                encounterId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Encounter '{encounterId}' was not found.");
    }

    private async Task EnsurePatientExistsAsync(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        _ = await patients
            .FindByIdAsync(
                hospitalContext.HospitalId,
                patientId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Patient '{patientId}' was not found.");
    }

    private async Task<PipelineSubjectBinding> ResolveEncounterAsync(
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        Encounter encounter = await encounters
            .FindByIdAsync(
                hospitalContext.HospitalId,
                encounterId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Encounter '{encounterId}' was not found.");

        if (encounter.Status != EncounterStatus.Open)
        {
            throw new InvalidStateException(
                $"Encounter '{encounterId}' is "
                + EncounterWorkflowHelpers.FormatStatus(encounter.Status)
                + "; pipelines can only be started for open encounters.");
        }

        return new PipelineSubjectBinding(encounter.PatientId, encounter.Id);
    }

    private async Task<PipelineSubjectBinding> ResolvePatientAsync(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        Patient patient = await patients
            .FindByIdAsync(
                hospitalContext.HospitalId,
                patientId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Patient '{patientId}' was not found.");

        if (patient.DeletedAt is not null)
        {
            throw new InvalidStateException(
                $"Patient '{patientId}' is deleted and cannot start a "
                + "new pipeline.");
        }

        return new PipelineSubjectBinding(patient.Id, EncounterId: null);
    }
}

/// <summary>Resolved patient/encounter binding for a pipeline subject.</summary>
public sealed record PipelineSubjectBinding(
    Guid PatientId,
    Guid? EncounterId);
