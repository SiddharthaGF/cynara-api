using System.Text.Json;

using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Forms;
using Cynara.Application.Modules.FormResponses.Persistence;
using Cynara.Application.Persistence;
using Cynara.Domain.Forms;

namespace Cynara.Application.Modules.FormResponses;

public sealed class FormResponseLifecycleService(
    IFormResponseRepository responses,
    IUnitOfWork unitOfWork,
    IFormResponseValidator responseValidator,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IFormResponseLifecycleService
{
    public async Task<FormResponseDto> CreateAsync(
        string code,
        string version,
        CreateFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(request);
        SemverRules.EnsureValid(version);
        FormVersion formVersion = await responses.FindPublishedVersionAsync(
                code,
                version,
                cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Published form '{code}' version '{version}' was not found.");
        if (formVersion.Status != FormVersionStatus.Published)
        {
            throw new InvalidStateException(
                formVersion.Status == FormVersionStatus.Retired
                    ? $"Retired form '{code}' version '{version}' cannot accept new responses."
                    : $"Responses cannot be created from unpublished form '{code}' version '{version}'.");
        }

        string answersJson = NormalizeAnswersJson(request.AnswersJson);
        answersJson = ValidateAndNormalizeAnswers(
            formVersion,
            answersJson,
            FormResponseValidationMode.Draft);
        DateTimeOffset now = timeProvider.GetUtcNow();
        var response = new FormResponse
        {
            Id = Guid.NewGuid(),
            FormVersionId = formVersion.Id,
            Status = FormResponseStatus.Draft,
            AnswersJson = answersJson,
            RevisionNumber = 1,
            RowVersion = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        FormResponseRevision revision = FormResponseWorkflowHelpers
            .CreateRevision(response, actorId, now);
        auditWriter.Append(
            AuditEntityTypes.FormResponse,
            response.Id,
            "response.created",
            actorId,
            now,
            new
            {
                formCode = code,
                formVersion = version,
                formVersionId = formVersion.Id,
                revisionNumber = response.RevisionNumber,
            });

        responses.Add(response, revision);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormResponseMappers.ToDto(response, formVersion);
    }

    public async Task<FormResponseDto> UpdateAsync(
        Guid id,
        UpdateFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FormResponse response = await FormResponseWorkflowHelpers
            .RequireResponseAsync(responses, id, track: true, includeDeleted: false, cancellationToken).ConfigureAwait(false);
        FormResponseWorkflowHelpers.EnsureDraft(response);
        FormResponseWorkflowHelpers.EnsureConcurrency(
            response,
            request.RowVersion);
        string answersJson = NormalizeAnswersJson(request.AnswersJson);
        answersJson = ValidateAndNormalizeAnswers(
            response.FormVersion,
            answersJson,
            FormResponseValidationMode.Draft);
        DateTimeOffset now = timeProvider.GetUtcNow();
        response.AnswersJson = answersJson;
        response.RevisionNumber++;
        response.RowVersion = request.RowVersion + 1;
        response.UpdatedAt = now;
        responses.AddRevision(FormResponseWorkflowHelpers.CreateRevision(
            response,
            actorId,
            now));
        auditWriter.Append(
            AuditEntityTypes.FormResponse,
            response.Id,
            "response.updated",
            actorId,
            now,
            new
            {
                revisionNumber = response.RevisionNumber,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormResponseMappers.ToDto(response, response.FormVersion);
    }

    public async Task<FormResponseDto> CompleteAsync(
        Guid id,
        CompleteFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FormResponse response = await FormResponseWorkflowHelpers
            .RequireResponseAsync(responses, id, track: true, includeDeleted: false, cancellationToken).ConfigureAwait(false);
        FormResponseWorkflowHelpers.EnsureDraft(response);
        FormResponseWorkflowHelpers.EnsureConcurrency(
            response,
            request.RowVersion);
        response.AnswersJson = ValidateAndNormalizeAnswers(
            response.FormVersion,
            response.AnswersJson,
            FormResponseValidationMode.Complete);
        DateTimeOffset now = timeProvider.GetUtcNow();
        response.Status = FormResponseStatus.Completed;
        response.RevisionNumber++;
        response.RowVersion = request.RowVersion + 1;
        response.UpdatedAt = now;
        response.CompletedAt = now;
        responses.AddRevision(FormResponseWorkflowHelpers.CreateRevision(
            response,
            actorId,
            now));
        auditWriter.Append(
            AuditEntityTypes.FormResponse,
            response.Id,
            "response.completed",
            actorId,
            now,
            new
            {
                revisionNumber = response.RevisionNumber,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormResponseMappers.ToDto(response, response.FormVersion);
    }

    public async Task SoftDeleteDraftAsync(
        Guid id,
        string? reason,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormResponse response = await FormResponseWorkflowHelpers
            .RequireResponseAsync(responses, id, track: true, includeDeleted: false, cancellationToken).ConfigureAwait(false);
        FormResponseWorkflowHelpers.EnsureDraft(response);
        DateTimeOffset now = timeProvider.GetUtcNow();
        response.DeletedAt = now;
        response.UpdatedAt = now;
        auditWriter.Append(
            AuditEntityTypes.FormResponse,
            response.Id,
            "response.draft.deleted",
            actorId,
            now,
            new
            {
                revisionNumber = response.RevisionNumber,
                reason,
            });
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ValidateAndNormalizeAnswers(
        FormVersion formVersion,
        string answersJson,
        FormResponseValidationMode mode)
    {
        FormResponseValidationResult validation = responseValidator.Validate(
            formVersion.ClinicalSchemaJson,
            formVersion.UiSchemaJson,
            formVersion.RulesSchemaJson,
            answersJson,
            mode);
        validation.EnsureValid();
        return validation.NormalizedAnswersJson;
    }

    private static string NormalizeAnswersJson(string? answersJson)
    {
        if (string.IsNullOrWhiteSpace(answersJson))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(answersJson);
            return document.RootElement.ValueKind != JsonValueKind.Object
                ? throw new ValidationException("Answers must be a JSON object.")
                : JsonSerializer.Serialize(
                    document.RootElement,
                    CanonicalJsonOptions.Instance);
        }
        catch (JsonException exception)
        {
            throw new ValidationException(
                $"Answers must be valid JSON: {exception.Message}");
        }
    }
}
