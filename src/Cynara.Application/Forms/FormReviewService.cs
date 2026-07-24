using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

public sealed class FormReviewService(
    IFormRepository forms,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ISchemaValidator schemaValidator,
    IFormCompiler formCompiler,
    TimeProvider timeProvider) : IFormReviewService
{
    public async Task<FormVersionDto> SubmitForReviewAsync(
        string code,
        SubmitFormDraftForReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        FormDefinition definition = await FormWorkflowHelpers
            .RequireDefinitionAsync(forms, code, track: true, cancellationToken).ConfigureAwait(false);
        FormVersion draft = FormWorkflowHelpers.RequireDraft(definition);
        FormWorkflowHelpers.EnsureDraftConcurrency(draft, request.RowVersion);

        schemaValidator.ValidateFormDraft(
            draft.ClinicalSchemaJson,
            draft.UiSchemaJson,
            draft.RulesSchemaJson);
        _ = await formCompiler.CompileAsync(
            draft.ClinicalSchemaJson,
            draft.UiSchemaJson,
            draft.RulesSchemaJson,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        FormVersionLifecycle.Fire(
            draft,
            FormVersionLifecycle.Trigger.SubmitForReview);
        draft.SubmittedForReviewAt = now;
        draft.LastReviewComment = null;
        draft.LastReviewDecision = null;
        draft.LastReviewedAt = null;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.FormVersion,
            draft.Id,
            "form.draft.submitted-for-review",
            actorId,
            now,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToVersionDto(definition, draft);
    }

    public async Task<FormVersionDto> WithdrawFromReviewAsync(
        string code,
        WithdrawFormDraftFromReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        FormDefinition definition = await FormWorkflowHelpers
            .RequireDefinitionAsync(forms, code, track: true, cancellationToken).ConfigureAwait(false);
        FormVersion review = FormWorkflowHelpers.RequireReviewVersion(definition);
        FormWorkflowHelpers.EnsureDraftConcurrency(review, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        FormVersionLifecycle.Fire(
            review,
            FormVersionLifecycle.Trigger.WithdrawFromReview);
        review.SubmittedForReviewAt = null;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.FormVersion,
            review.Id,
            "form.draft.withdrawn-from-review",
            actorId,
            now,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToVersionDto(definition, review);
    }

    public async Task<FormVersionDto> RejectReviewAsync(
        string code,
        RejectFormReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            throw new ValidationException("Review rejection comment is required.");
        }

        FormDefinition definition = await FormWorkflowHelpers
            .RequireDefinitionAsync(forms, code, track: true, cancellationToken).ConfigureAwait(false);
        FormVersion review = FormWorkflowHelpers.RequireReviewVersion(definition);
        FormWorkflowHelpers.EnsureDraftConcurrency(review, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        FormVersionLifecycle.Fire(
            review,
            FormVersionLifecycle.Trigger.RejectReview);
        review.SubmittedForReviewAt = null;
        review.LastReviewComment = request.Comment.Trim();
        review.LastReviewDecision = "rejected";
        review.LastReviewedAt = now;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.FormVersion,
            review.Id,
            "form.draft.rejected-from-review",
            actorId,
            now,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
                comment = review.LastReviewComment,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToVersionDto(definition, review);
    }

    public async Task<FormVersionDto> PublishDraftAsync(
        string code,
        PublishFormDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        FormDefinition definition = await FormWorkflowHelpers
            .RequireDefinitionAsync(forms, code, track: true, cancellationToken).ConfigureAwait(false);
        FormVersion review = FormWorkflowHelpers.RequireReviewVersion(definition);
        FormWorkflowHelpers.EnsureDraftConcurrency(review, request.RowVersion);

        schemaValidator.ValidateFormDraft(
            review.ClinicalSchemaJson,
            review.UiSchemaJson,
            review.RulesSchemaJson);

        FormCompilationResult compiled = await formCompiler.CompileAsync(
            review.ClinicalSchemaJson,
            review.UiSchemaJson,
            review.RulesSchemaJson,
            cancellationToken).ConfigureAwait(false);

        string version = SemverRules.NextVersion(
            definition.Versions
                .Where(item => item.Status == FormVersionStatus.Published
                    && item.Version != null)
                .Select(item => item.Version!));

        DateTimeOffset now = timeProvider.GetUtcNow();
        review.Version = version;
        FormVersionLifecycle.Fire(
            review,
            FormVersionLifecycle.Trigger.Publish);
        review.ClinicalSchemaJson = compiled.ClinicalSchemaJson;
        review.UiSchemaJson = compiled.UiSchemaJson;
        review.RulesSchemaJson = compiled.RulesSchemaJson;
        review.DependencyMetadataJson = compiled.DependencyMetadataJson;
        review.ContentHash = compiled.ContentHash;
        review.PublishedSchemaVersion = FormMappers.ReadSchemaVersion(
            compiled.ClinicalSchemaJson);
        review.PublishedAt = now;
        review.SubmittedForReviewAt = null;
        review.LastReviewDecision = "approved";
        review.LastReviewedAt = now;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.FormVersion,
            review.Id,
            "form.version.published",
            actorId,
            now,
            new
            {
                code = definition.Code,
                version,
                schemaVersion = review.PublishedSchemaVersion,
                contentHash = review.ContentHash,
                dependencyMetadata = compiled.DependencyMetadataJson,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToVersionDto(definition, review);
    }
}
