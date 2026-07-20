using System.Text.Json;

using Cynara.Application.Common;
using Cynara.Application.Persistence;
using Cynara.Domain.Audit;
using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

public sealed class FormResponseService(
    IFormResponseRepository responses,
    IFormResponseValidator responseValidator,
    IAuditRepository audit,
    TimeProvider timeProvider) : IFormResponseService
{
    public async Task<FormResponseDto> CreateAsync(
        string code,
        string version,
        CreateFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        SemverRules.EnsureValid(version);

        FormVersion? formVersion = await responses.FindPublishedVersionAsync(code, version, cancellationToken)
            ?? throw new NotFoundException($"Published form '{code}' version '{version}' was not found.");

        if (formVersion.Status != FormVersionStatus.Published)
        {
            throw new InvalidStateException(
                formVersion.Status == FormVersionStatus.Retired
                    ? $"Retired form '{code}' version '{version}' cannot accept new responses."
                    : $"Responses cannot be created from unpublished form '{code}' version '{version}'.");
        }

        string answersJson = NormalizeAnswersJson(request.AnswersJson);
        answersJson = ValidateAndNormalizeAnswers(formVersion, answersJson, FormResponseValidationMode.Draft);
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

        FormResponseRevision revision = CreateRevision(response, actorId, now);

        AppendAudit("form-response", response.Id, "response.created", actorId, now, new
        {
            formCode = code,
            formVersion = version,
            formVersionId = formVersion.Id,
            revisionNumber = response.RevisionNumber,
        });

        await responses.AddAsync(response, revision, cancellationToken);
        return ToDto(response, formVersion);
    }

    public async Task<FormResponseDto> GetAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken)
    {
        FormResponse response = await RequireResponseAsync(id, track: false, includeDeleted, cancellationToken);
        return ToDto(response, response.FormVersion);
    }

    public async Task<FormResponseDto> UpdateAsync(
        Guid id,
        UpdateFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormResponse response = await RequireResponseAsync(id, track: true, includeDeleted: false, cancellationToken);
        EnsureDraft(response);
        EnsureConcurrency(response, request.RowVersion);

        string answersJson = NormalizeAnswersJson(request.AnswersJson);
        answersJson = ValidateAndNormalizeAnswers(
            response.FormVersion,
            answersJson,
            FormResponseValidationMode.Draft);
        DateTimeOffset now = timeProvider.GetUtcNow();
        response.AnswersJson = answersJson;
        response.RevisionNumber += 1;
        response.RowVersion = request.RowVersion + 1;
        response.UpdatedAt = now;

        FormResponseRevision revision = CreateRevision(response, actorId, now);
        responses.AddRevision(revision);

        AppendAudit("form-response", response.Id, "response.updated", actorId, now, new
        {
            revisionNumber = response.RevisionNumber,
            rowVersion = request.RowVersion,
        });

        await responses.SaveChangesAsync(cancellationToken);
        return ToDto(response, response.FormVersion);
    }

    public async Task<FormResponseDto> CompleteAsync(
        Guid id,
        CompleteFormResponseRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormResponse response = await RequireResponseAsync(id, track: true, includeDeleted: false, cancellationToken);
        EnsureDraft(response);
        EnsureConcurrency(response, request.RowVersion);

        string answersJson = ValidateAndNormalizeAnswers(
            response.FormVersion,
            response.AnswersJson,
            FormResponseValidationMode.Complete);
        DateTimeOffset now = timeProvider.GetUtcNow();
        response.AnswersJson = answersJson;
        response.Status = FormResponseStatus.Completed;
        response.RevisionNumber += 1;
        response.RowVersion = request.RowVersion + 1;
        response.UpdatedAt = now;
        response.CompletedAt = now;

        FormResponseRevision revision = CreateRevision(response, actorId, now);
        responses.AddRevision(revision);

        AppendAudit("form-response", response.Id, "response.completed", actorId, now, new
        {
            revisionNumber = response.RevisionNumber,
            rowVersion = request.RowVersion,
        });

        await responses.SaveChangesAsync(cancellationToken);
        return ToDto(response, response.FormVersion);
    }

    public async Task SoftDeleteDraftAsync(
        Guid id,
        string? reason,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormResponse response = await RequireResponseAsync(id, track: true, includeDeleted: false, cancellationToken);
        EnsureDraft(response);

        DateTimeOffset now = timeProvider.GetUtcNow();
        response.DeletedAt = now;
        response.UpdatedAt = now;

        AppendAudit("form-response", response.Id, "response.draft.deleted", actorId, now, new
        {
            revisionNumber = response.RevisionNumber,
            reason,
        });

        await responses.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FormResponseRevisionDto>> ListRevisionsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        _ = await RequireResponseAsync(id, track: false, includeDeleted: true, cancellationToken);
        IReadOnlyList<FormResponseRevision> revisions = await responses.ListRevisionsAsync(id, cancellationToken);
        return [.. revisions.Select(ToRevisionDto)];
    }

    public async Task<FormResponseRevisionDto> GetRevisionAsync(
        Guid id,
        uint revisionNumber,
        CancellationToken cancellationToken)
    {
        _ = await RequireResponseAsync(id, track: false, includeDeleted: true, cancellationToken);
        FormResponseRevision? revision = await responses.FindRevisionAsync(id, revisionNumber, cancellationToken)
            ?? throw new NotFoundException(
                $"Revision {revisionNumber} for response '{id}' was not found.");

        return ToRevisionDto(revision);
    }

    private async Task<FormResponse> RequireResponseAsync(
        Guid id,
        bool track,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        FormResponse? response = await responses.FindByIdAsync(id, track, includeDeleted, cancellationToken);
        return response ?? throw new NotFoundException($"Form response '{id}' was not found.");
    }

    private static void EnsureDraft(FormResponse response)
    {
        if (response.Status != FormResponseStatus.Draft)
        {
            throw new InvalidStateException("Only draft responses can be modified.");
        }
    }

    private static void EnsureConcurrency(FormResponse response, uint expectedRowVersion)
    {
        if (response.RowVersion != expectedRowVersion)
        {
            throw new ConcurrencyException("The form response was modified by another request.");
        }
    }

    private static FormResponseRevision CreateRevision(FormResponse response, string? actorId, DateTimeOffset now)
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
                : JsonSerializer.Serialize(document.RootElement, CanonicalJsonOptions.Instance);
        }
        catch (JsonException exception)
        {
            throw new ValidationException($"Answers must be valid JSON: {exception.Message}");
        }
    }

    private void AppendAudit(
        string resourceType,
        Guid resourceId,
        string action,
        string? actorId,
        DateTimeOffset occurredAt,
        object metadata)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            ActorId = actorId,
            OccurredAt = occurredAt,
            MetadataJson = JsonSerializer.Serialize(metadata, CanonicalJsonOptions.Instance),
        });
    }

    private static FormResponseDto ToDto(FormResponse response, FormVersion formVersion)
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

    private static FormResponseRevisionDto ToRevisionDto(FormResponseRevision revision)
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
