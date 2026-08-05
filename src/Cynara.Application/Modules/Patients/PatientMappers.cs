using Cynara.Domain.Patients;

namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Stateless mapping helpers that project <see cref="Patient"/> entities
/// to the public <see cref="PatientDto"/> shape. Enum values are rendered
/// as lowercase strings to keep the JSON contract stable across layers.
/// </summary>
internal static class PatientMappers
{
    public static PatientDto ToDto(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        return new PatientDto(
            patient.Id,
            patient.Mrn,
            patient.NationalId,
            patient.GivenName,
            patient.FamilyName,
            patient.BirthDate,
            patient.Sex.ToString().ToLowerInvariant(),
            patient.Status.ToString().ToLowerInvariant(),
            patient.RowVersion,
            patient.DeletedAt,
            patient.CreatedAt,
            patient.UpdatedAt);
    }
}
