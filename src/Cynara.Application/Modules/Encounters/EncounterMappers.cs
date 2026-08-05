using Cynara.Domain.Encounters;

namespace Cynara.Application.Modules.Encounters;

/// <summary>
/// Stateless mapping helpers that project <see cref="Encounter"/> entities
/// to the public <see cref="EncounterDto"/> shape.
/// </summary>
internal static class EncounterMappers
{
    public static EncounterDto ToDto(Encounter encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return new EncounterDto(
            encounter.Id,
            encounter.PatientId,
            encounter.FacilityId,
            encounter.ClinicalAreaId,
            EncounterWorkflowHelpers.FormatType(encounter.Type),
            encounter.ResponsibleProfessionalId,
            EncounterWorkflowHelpers.FormatStatus(encounter.Status),
            encounter.StartedAt,
            encounter.EndedAt,
            encounter.RowVersion,
            encounter.CreatedAt,
            encounter.UpdatedAt);
    }
}
