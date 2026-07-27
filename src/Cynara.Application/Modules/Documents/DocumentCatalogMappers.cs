using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

internal static class DocumentCatalogMappers
{
    public static DocumentDefinitionDto ToDto(DocumentDefinition documentDefinition)
    {
        ArgumentNullException.ThrowIfNull(documentDefinition);
        return new DocumentDefinitionDto(
            documentDefinition.Id,
            documentDefinition.Code,
            documentDefinition.Name,
            documentDefinition.Status.ToString().ToLowerInvariant(),
            documentDefinition.FormDefinitionId,
            documentDefinition.FormVersionId,
            documentDefinition.FacilityId,
            documentDefinition.ClinicalAreaId,
            documentDefinition.DisciplineId,
            documentDefinition.AllowsMultipleInstancesPerEncounter,
            documentDefinition.RequiresActorForCreation,
            documentDefinition.RequiresActorForCompletion,
            documentDefinition.RowVersion,
            documentDefinition.RetiredAt,
            documentDefinition.CreatedAt,
            documentDefinition.UpdatedAt);
    }
}
