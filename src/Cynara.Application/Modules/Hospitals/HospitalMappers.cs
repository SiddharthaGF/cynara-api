using Cynara.Domain.Hospitals;

namespace Cynara.Application.Modules.Hospitals;

internal static class HospitalMappers
{
    public static HospitalWorkspaceDto ToDto(Hospital hospital)
    {
        ArgumentNullException.ThrowIfNull(hospital);
        return new HospitalWorkspaceDto(
            hospital.Id,
            hospital.Code,
            hospital.Name,
            hospital.Status.ToString().ToLowerInvariant(),
            hospital.MetadataJson,
            hospital.RowVersion,
            hospital.CreatedAt,
            hospital.UpdatedAt);
    }
}
