using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

internal static class ClinicalTaxonomyMappers
{
    public static FacilityDto ToDto(Facility facility)
    {
        ArgumentNullException.ThrowIfNull(facility);
        return new FacilityDto(
            facility.Id,
            facility.Code,
            facility.Name,
            facility.Status.ToString().ToLowerInvariant(),
            facility.RowVersion,
            facility.RetiredAt,
            facility.CreatedAt,
            facility.UpdatedAt);
    }

    public static ClinicalAreaDto ToDto(ClinicalArea clinicalArea)
    {
        ArgumentNullException.ThrowIfNull(clinicalArea);
        return new ClinicalAreaDto(
            clinicalArea.Id,
            clinicalArea.Code,
            clinicalArea.Name,
            clinicalArea.FacilityId,
            clinicalArea.Status.ToString().ToLowerInvariant(),
            clinicalArea.RowVersion,
            clinicalArea.RetiredAt,
            clinicalArea.CreatedAt,
            clinicalArea.UpdatedAt);
    }

    public static DisciplineDto ToDto(Discipline discipline)
    {
        ArgumentNullException.ThrowIfNull(discipline);
        return new DisciplineDto(
            discipline.Id,
            discipline.Code,
            discipline.Name,
            discipline.ClinicalAreaId,
            discipline.Status.ToString().ToLowerInvariant(),
            discipline.RowVersion,
            discipline.RetiredAt,
            discipline.CreatedAt,
            discipline.UpdatedAt);
    }
}
