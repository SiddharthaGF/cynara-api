using System.Text.Json;

using Cynara.Application.Common;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Domain.Audit;
using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

public sealed class FormService(
    IFormRepository forms,
    IAuditRepository audit,
    ISchemaValidator schemaValidator,
    IFormCompiler formCompiler,
    TimeProvider timeProvider) : IFormService
{
    public async Task<FormSummaryDto> CreateAsync(CreateFormRequest request, string? actorId, CancellationToken cancellationToken)
    {
        FormCodeRules.EnsureValid(request.Code);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Form name is required.");
        }

        schemaValidator.ValidateFormDraft(request.ClinicalSchemaJson, request.UiSchemaJson, request.RulesSchemaJson);

        if (await forms.CodeExistsAsync(request.Code, cancellationToken))
        {
            throw new ConflictException($"Form '{request.Code}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var definition = new FormDefinition
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        var draft = new FormVersion
        {
            Id = Guid.NewGuid(),
            FormDefinitionId = definition.Id,
            Status = FormVersionStatus.Draft,
            ClinicalSchemaJson = request.ClinicalSchemaJson,
            UiSchemaJson = request.UiSchemaJson,
            RulesSchemaJson = request.RulesSchemaJson,
            CreatedAt = now,
        };

        AppendAudit("form-definition", definition.Id, "form.created", actorId, now, new
        {
            code = definition.Code,
            draftVersionId = draft.Id,
        });

        await forms.AddDefinitionAsync(definition, draft, cancellationToken);
        return ToSummary(definition);
    }

    public async Task<IReadOnlyList<FormSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<FormDefinition> items = await forms.ListDefinitionsAsync(cancellationToken);
        return [.. items.Select(ToSummary)];
    }

    public async Task<FormSummaryDto> GetSummaryAsync(string code, CancellationToken cancellationToken)
    {
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken);
        return ToSummary(definition);
    }

    public async Task<FormVersionDto> GetEditableVersionAsync(string code, CancellationToken cancellationToken)
    {
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken);
        FormVersion editable = RequireEditableVersion(definition);
        return ToVersionDto(definition, editable);
    }

    public async Task<FormVersionDto> GetVersionAsync(string code, string version, CancellationToken cancellationToken)
    {
        SemverRules.EnsureValid(version);
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken);
        FormVersion? published = definition.Versions.SingleOrDefault(
            item => item.Version == version && item.Status != FormVersionStatus.Draft && item.Status != FormVersionStatus.Review)
            ?? throw new NotFoundException($"Form '{code}' version '{version}' was not found.");
        return ToVersionDto(definition, published);
    }

    public async Task<FormVersionDto> UpdateDraftAsync(
        string code,
        UpdateFormDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        schemaValidator.ValidateFormDraft(request.ClinicalSchemaJson, request.UiSchemaJson, request.RulesSchemaJson);

        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        FormVersion draft = RequireDraft(definition);
        EnsureDraftConcurrency(draft, request.RowVersion);

        draft.ClinicalSchemaJson = request.ClinicalSchemaJson;
        draft.UiSchemaJson = request.UiSchemaJson;
        draft.RulesSchemaJson = request.RulesSchemaJson;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        AppendAudit("form-version", draft.Id, "form.draft.updated", actorId, definition.UpdatedAt, new
        {
            code = definition.Code,
            rowVersion = request.RowVersion,
        });

        await forms.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, draft);
    }

    public async Task<FormVersionDto> SubmitForReviewAsync(
        string code,
        SubmitFormDraftForReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        FormVersion draft = RequireDraft(definition);
        EnsureDraftConcurrency(draft, request.RowVersion);

        schemaValidator.ValidateFormDraft(draft.ClinicalSchemaJson, draft.UiSchemaJson, draft.RulesSchemaJson);
        await EnsureCanCompileAsync(draft, cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        draft.Status = FormVersionStatus.Review;
        draft.SubmittedForReviewAt = now;
        draft.LastReviewComment = null;
        draft.LastReviewDecision = null;
        draft.LastReviewedAt = null;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        AppendAudit("form-version", draft.Id, "form.draft.submitted-for-review", actorId, now, new
        {
            code = definition.Code,
            rowVersion = request.RowVersion,
        });

        await forms.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, draft);
    }

    public async Task<FormVersionDto> WithdrawFromReviewAsync(
        string code,
        WithdrawFormDraftFromReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        FormVersion review = RequireReviewVersion(definition);
        EnsureDraftConcurrency(review, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        review.Status = FormVersionStatus.Draft;
        review.SubmittedForReviewAt = null;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        AppendAudit("form-version", review.Id, "form.draft.withdrawn-from-review", actorId, now, new
        {
            code = definition.Code,
            rowVersion = request.RowVersion,
        });

        await forms.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, review);
    }

    public async Task<FormVersionDto> RejectReviewAsync(
        string code,
        RejectFormReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            throw new ValidationException("Review rejection comment is required.");
        }

        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        FormVersion review = RequireReviewVersion(definition);
        EnsureDraftConcurrency(review, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        review.Status = FormVersionStatus.Draft;
        review.SubmittedForReviewAt = null;
        review.LastReviewComment = request.Comment.Trim();
        review.LastReviewDecision = "rejected";
        review.LastReviewedAt = now;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        AppendAudit("form-version", review.Id, "form.draft.rejected-from-review", actorId, now, new
        {
            code = definition.Code,
            rowVersion = request.RowVersion,
            comment = review.LastReviewComment,
        });

        await forms.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, review);
    }

    public async Task<FormVersionDto> PublishDraftAsync(
        string code,
        PublishFormDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        FormVersion review = RequireReviewVersion(definition);
        EnsureDraftConcurrency(review, request.RowVersion);

        schemaValidator.ValidateFormDraft(review.ClinicalSchemaJson, review.UiSchemaJson, review.RulesSchemaJson);

        FormCompilationResult compiled = await formCompiler.CompileAsync(
            review.ClinicalSchemaJson,
            review.UiSchemaJson,
            review.RulesSchemaJson,
            cancellationToken);

        string version = SemverRules.NextVersion(
            definition.Versions
                .Where(item => item.Status == FormVersionStatus.Published && item.Version != null)
                .Select(item => item.Version!));

        DateTimeOffset now = timeProvider.GetUtcNow();
        review.Version = version;
        review.Status = FormVersionStatus.Published;
        review.ClinicalSchemaJson = compiled.ClinicalSchemaJson;
        review.UiSchemaJson = compiled.UiSchemaJson;
        review.RulesSchemaJson = compiled.RulesSchemaJson;
        review.DependencyMetadataJson = compiled.DependencyMetadataJson;
        review.ContentHash = compiled.ContentHash;
        review.PublishedSchemaVersion = ReadSchemaVersion(compiled.ClinicalSchemaJson);
        review.PublishedAt = now;
        review.SubmittedForReviewAt = null;
        review.LastReviewDecision = "approved";
        review.LastReviewedAt = now;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        AppendAudit("form-version", review.Id, "form.version.published", actorId, now, new
        {
            code = definition.Code,
            version,
            schemaVersion = review.PublishedSchemaVersion,
            contentHash = review.ContentHash,
            dependencyMetadata = compiled.DependencyMetadataJson,
        });

        await forms.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, review);
    }

    public async Task<FormVersionDto> CreateDraftFromLatestAsync(string code, string? actorId, CancellationToken cancellationToken)
    {
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);

        if (definition.Versions.Any(item => item.Status is FormVersionStatus.Draft or FormVersionStatus.Review))
        {
            throw new ConflictException($"Form '{code}' already has an editable version.");
        }

        FormVersion? source = definition.Versions
            .Where(item => item.Status == FormVersionStatus.Published && item.Version != null)
            .OrderBy(item => item.Version!, SemverRules.StringComparer)
            .LastOrDefault();

        DateTimeOffset now = timeProvider.GetUtcNow();
        var draft = new FormVersion
        {
            Id = Guid.NewGuid(),
            FormDefinitionId = definition.Id,
            Status = FormVersionStatus.Draft,
            ClinicalSchemaJson = source?.ClinicalSchemaJson ?? DefaultClinicalSchema(),
            UiSchemaJson = source?.UiSchemaJson,
            RulesSchemaJson = source?.RulesSchemaJson,
            CreatedAt = now,
        };

        AppendAudit("form-version", draft.Id, "form.draft.created", actorId, now, new
        {
            code = definition.Code,
            sourceVersion = source?.Version,
        });

        await forms.AddVersionAsync(draft, cancellationToken);
        return ToVersionDto(definition, draft);
    }

    public async Task<FormVersionDto> RetireVersionAsync(
        string code,
        string version,
        string? actorId,
        CancellationToken cancellationToken)
    {
        SemverRules.EnsureValid(version);

        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        FormVersion published = definition.Versions.SingleOrDefault(
            item => item.Version == version && item.Status == FormVersionStatus.Published)
            ?? throw new NotFoundException($"Published form '{code}' version '{version}' was not found.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        published.Status = FormVersionStatus.Retired;
        published.RetiredAt = now;
        definition.UpdatedAt = now;

        AppendAudit("form-version", published.Id, "form.version.retired", actorId, now, new
        {
            code = definition.Code,
            version,
        });

        await forms.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, published);
    }

    public async Task SoftDeleteDraftAsync(
        string code,
        string? reason,
        string? actorId,
        CancellationToken cancellationToken)
    {
        FormDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        FormVersion editable = RequireEditableVersion(definition);

        bool hasPublishedVersions = definition.Versions.Any(item => item.Status == FormVersionStatus.Published);
        if (hasPublishedVersions)
        {
            throw new InvalidStateException(
                $"Form '{code}' cannot be soft-deleted while published versions exist. Retire active versions first.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        forms.RemoveVersion(editable);
        definition.DeletedAt = now;
        definition.UpdatedAt = now;

        AppendAudit("form-definition", definition.Id, "form.draft.deleted", actorId, now, new
        {
            code = definition.Code,
            draftVersionId = editable.Id,
            reason,
        });

        await forms.SaveChangesAsync(cancellationToken);
    }

    private async Task<FormDefinition> RequireDefinitionAsync(
        string code,
        CancellationToken cancellationToken,
        bool track = false)
    {
        FormDefinition? definition = await forms.FindDefinitionByCodeAsync(code, track, cancellationToken);
        return definition ?? throw new NotFoundException($"Form '{code}' was not found.");
    }

    private static FormVersion RequireEditableVersion(FormDefinition definition)
    {
        return definition.Versions.SingleOrDefault(item => item.Status is FormVersionStatus.Draft or FormVersionStatus.Review)
            ?? throw new NotFoundException($"Form '{definition.Code}' has no editable version.");
    }

    private static FormVersion RequireDraft(FormDefinition definition)
    {
        return definition.Versions.SingleOrDefault(item => item.Status == FormVersionStatus.Draft)
            ?? throw new NotFoundException($"Form '{definition.Code}' has no draft version.");
    }

    private static FormVersion RequireReviewVersion(FormDefinition definition)
    {
        return definition.Versions.SingleOrDefault(item => item.Status == FormVersionStatus.Review)
            ?? throw new NotFoundException($"Form '{definition.Code}' has no version in review.");
    }

    private async Task EnsureCanCompileAsync(FormVersion version, CancellationToken cancellationToken)
    {
        _ = await formCompiler.CompileAsync(
            version.ClinicalSchemaJson,
            version.UiSchemaJson,
            version.RulesSchemaJson,
            cancellationToken);
    }

    private static string ReadSchemaVersion(string clinicalSchemaJson)
    {
        using var document = JsonDocument.Parse(clinicalSchemaJson);
        return document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaVersion)
            ? schemaVersion.GetString() ?? "1.0.0"
            : "1.0.0";
    }

    private static void EnsureDraftConcurrency(FormVersion version, uint expectedRowVersion)
    {
        if (version.RowVersion != expectedRowVersion)
        {
            throw new ConcurrencyException("The form draft was modified by another request.");
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

    private static FormSummaryDto ToSummary(FormDefinition definition)
    {
        FormVersion? editable = definition.Versions.SingleOrDefault(
            item => item.Status is FormVersionStatus.Draft or FormVersionStatus.Review);
        var publishedVersions = definition.Versions
            .Where(item => item.Status == FormVersionStatus.Published && item.Version != null)
            .Select(item => item.Version!)
            .OrderBy(static version => version, SemverRules.StringComparer)
            .ToList();

        return new FormSummaryDto(
            definition.Code,
            definition.Name,
            definition.CreatedAt,
            definition.UpdatedAt,
            editable?.Id.ToString(),
            editable?.Status.ToString().ToLowerInvariant(),
            editable?.RowVersion,
            publishedVersions);
    }

    private static FormVersionDto ToVersionDto(FormDefinition definition, FormVersion version)
    {
        return new FormVersionDto(
            version.Id,
            definition.Code,
            version.Version,
            version.Status.ToString().ToLowerInvariant(),
            version.ClinicalSchemaJson,
            version.UiSchemaJson,
            version.RulesSchemaJson,
            version.ContentHash,
            version.DependencyMetadataJson,
            version.RowVersion,
            version.CreatedAt,
            version.SubmittedForReviewAt,
            version.PublishedAt,
            version.RetiredAt,
            version.PublishedSchemaVersion,
            version.LastReviewComment,
            version.LastReviewDecision,
            version.LastReviewedAt);
    }

    private static string DefaultClinicalSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                {
                  "id": "placeholder",
                  "code": "form.placeholder",
                  "type": "text"
                }
              ]
            }
            """;
    }
}
