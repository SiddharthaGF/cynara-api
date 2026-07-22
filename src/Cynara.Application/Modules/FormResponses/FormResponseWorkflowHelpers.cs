using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.FormResponses;

internal static class FormResponseWorkflowHelpers
{
    public static async Task<FormResponse> RequireResponseAsync(
        IFormResponseRepository responses,
        Guid id,
        bool track,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        FormResponse? response = await responses.FindByIdAsync(
            id,
            track,
            includeDeleted,
            cancellationToken).ConfigureAwait(false);
        return response ?? throw new NotFoundException(
            $"Form response '{id}' was not found.");
    }

    public static void EnsureDraft(FormResponse response)
    {
        if (response.Status != FormResponseStatus.Draft)
        {
            throw new InvalidStateException(
                "Only draft responses can be modified.");
        }
    }

    public static void EnsureConcurrency(
        FormResponse response,
        uint expectedRowVersion)
    {
        if (response.RowVersion != expectedRowVersion)
        {
            throw new ConcurrencyException(
                "The form response was modified by another request.");
        }
    }

    public static FormResponseRevision CreateRevision(
        FormResponse response,
        string? actorId,
        DateTimeOffset now)
    {
        return new FormResponseRevision
        {
            Id = Guid.NewGuid(),
            FormResponseId = response.Id,
            RevisionNumber = response.RevisionNumber,
            AnswersJson = response.AnswersJson,
            Status = response.Status,
            ActorId = actorId,
            CreatedAt = now,
        };
    }
}
