using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Stateless mapping helpers that project <see cref="ClinicalDocument"/>
/// entities to the public <see cref="ClinicalDocumentDto"/> shape.
/// </summary>
internal static class ClinicalDocumentMappers
{
    public static ClinicalDocumentDto ToDto(ClinicalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new ClinicalDocumentDto(
            document.Id,
            document.DocumentDefinitionId,
            document.PatientId,
            document.EncounterId,
            document.FormVersionId,
            document.FormResponseId,
            document.AuthorId,
            ClinicalDocumentWorkflowHelpers.FormatStatus(document.Status),
            document.CreatedAt,
            document.CompletedAt,
            document.UpdatedAt,
            document.RowVersion);
    }
}
