using Cynara.Application.Forms;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.FormResponses;

internal static class FormResponseMappers
{
    public static FormResponseDto ToDto(
        FormResponse response,
        FormVersion formVersion)
    {
        return new FormResponseDto(
            response.Id,
            formVersion.FormDefinition.Code,
            formVersion.Version!,
            formVersion.Id,
            response.Status.ToString().ToLowerInvariant(),
            response.AnswersJson,
            response.RevisionNumber,
            response.RowVersion,
            response.CreatedAt,
            response.UpdatedAt,
            response.CompletedAt,
            response.DeletedAt);
    }

    public static FormResponseRevisionDto ToRevisionDto(
        FormResponseRevision revision)
    {
        return new FormResponseRevisionDto(
            revision.Id,
            revision.FormResponseId,
            revision.RevisionNumber,
            revision.AnswersJson,
            revision.Status.ToString().ToLowerInvariant(),
            revision.ActorId,
            revision.CreatedAt);
    }
}
