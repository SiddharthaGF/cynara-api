using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Workflows.Persistence;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Application.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Owns the workflow-definition lifecycle: draft CRUD, the
/// draft → review → published → retired state machine, immutable publishing,
/// and retirement. Every mutation validates the workflow graph contract,
/// stamps tenant scope, and emits audit events through the unit of work.
/// </summary>
public sealed class WorkflowLifecycleService(
    IWorkflowRepository workflows,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ISchemaValidator schemaValidator,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    ICapabilityGuard capabilityGuard) : IWorkflowLifecycleService
{
    private const string DefaultWorkflowSchema =
        /*lang=json,strict*/ """
        {
          "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
          "schemaVersion": "1.0.0",
          "nodes": [
            { "id": "start", "type": "start", "name": "Workflow starts" },
            { "id": "end", "type": "end", "name": "Completed" }
          ],
          "edges": [
            { "from": "start", "to": "end", "label": "Begin" }
          ]
        }
        """;

    public async Task<WorkflowSummaryDto> CreateAsync(
        CreateWorkflowRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        CodeRules.EnsureValid(request.Code, "Workflow");
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Workflow name is required.");
        }

        schemaValidator.ValidateWorkflowDraft(request.WorkflowSchemaJson);
        if (await workflows.CodeExistsAsync(
                request.Code,
                hospitalContext.HospitalId,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Workflow '{request.Code}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var definition = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            Code = request.Code,
            Name = request.Name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var draft = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            WorkflowDefinitionId = definition.Id,
            Status = WorkflowVersionStatus.Draft,
            WorkflowSchemaJson = request.WorkflowSchemaJson,
            CreatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.WorkflowDefinition,
            definition.Id,
            "workflow.created",
            actorId,
            now,
            new
            {
                code = definition.Code,
                draftVersionId = draft.Id,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        workflows.AddDefinition(definition, draft);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToSummary(definition);
    }

    public async Task<WorkflowVersionDto> UpdateDraftAsync(
        string code,
        UpdateWorkflowDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        schemaValidator.ValidateWorkflowDraft(request.WorkflowSchemaJson);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        WorkflowVersion draft = WorkflowWorkflowHelpers.RequireDraft(definition);
        WorkflowWorkflowHelpers.EnsureDraftConcurrency(draft, request.RowVersion);

        draft.WorkflowSchemaJson = request.WorkflowSchemaJson;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        auditWriter.Append(
            AuditEntityTypes.WorkflowVersion,
            draft.Id,
            "workflow.draft.updated",
            actorId,
            definition.UpdatedAt,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToVersionDto(definition, draft);
    }

    public async Task<WorkflowVersionDto> SubmitForReviewAsync(
        string code,
        SubmitWorkflowDraftForReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        WorkflowVersion draft = WorkflowWorkflowHelpers.RequireDraft(definition);
        WorkflowWorkflowHelpers.EnsureDraftConcurrency(draft, request.RowVersion);

        schemaValidator.ValidateWorkflowDraft(draft.WorkflowSchemaJson);

        DateTimeOffset now = timeProvider.GetUtcNow();
        WorkflowVersionLifecycle.Fire(
            draft,
            WorkflowVersionLifecycle.Trigger.SubmitForReview);
        draft.SubmittedForReviewAt = now;
        draft.LastReviewComment = null;
        draft.LastReviewDecision = null;
        draft.LastReviewedAt = null;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.WorkflowVersion,
            draft.Id,
            "workflow.draft.submitted-for-review",
            actorId,
            now,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToVersionDto(definition, draft);
    }

    public async Task<WorkflowVersionDto> WithdrawFromReviewAsync(
        string code,
        WithdrawWorkflowDraftFromReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        WorkflowVersion review = WorkflowWorkflowHelpers.RequireReviewVersion(definition);
        WorkflowWorkflowHelpers.EnsureDraftConcurrency(review, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        WorkflowVersionLifecycle.Fire(
            review,
            WorkflowVersionLifecycle.Trigger.WithdrawFromReview);
        review.SubmittedForReviewAt = null;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.WorkflowVersion,
            review.Id,
            "workflow.draft.withdrawn-from-review",
            actorId,
            now,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToVersionDto(definition, review);
    }

    public async Task<WorkflowVersionDto> RejectReviewAsync(
        string code,
        RejectWorkflowReviewRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            throw new ValidationException("Review rejection comment is required.");
        }

        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        WorkflowVersion review = WorkflowWorkflowHelpers.RequireReviewVersion(definition);
        WorkflowWorkflowHelpers.EnsureDraftConcurrency(review, request.RowVersion);

        DateTimeOffset now = timeProvider.GetUtcNow();
        WorkflowVersionLifecycle.Fire(
            review,
            WorkflowVersionLifecycle.Trigger.RejectReview);
        review.SubmittedForReviewAt = null;
        review.LastReviewComment = request.Comment.Trim();
        review.LastReviewDecision = "rejected";
        review.LastReviewedAt = now;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.WorkflowVersion,
            review.Id,
            "workflow.draft.rejected-from-review",
            actorId,
            now,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
                comment = review.LastReviewComment,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToVersionDto(definition, review);
    }

    public async Task<WorkflowVersionDto> PublishDraftAsync(
        string code,
        PublishWorkflowDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        WorkflowVersion review = WorkflowWorkflowHelpers.RequireReviewVersion(definition);
        WorkflowWorkflowHelpers.EnsureDraftConcurrency(review, request.RowVersion);

        // Publishing validates in the strict "published" context, which pins
        // form versions referenced by task nodes.
        schemaValidator.ValidateWorkflowForPublish(review.WorkflowSchemaJson);

        string version = SemverRules.NextVersion(
            definition.Versions
                .Where(item => item.Status == WorkflowVersionStatus.Published
                    && item.Version != null)
                .Select(item => item.Version!));
        DateTimeOffset now = timeProvider.GetUtcNow();
        review.Version = version;
        WorkflowVersionLifecycle.Fire(
            review,
            WorkflowVersionLifecycle.Trigger.Publish);
        review.ContentHash = ContentHashCalculator.Compute(
            review.WorkflowSchemaJson,
            uiSchemaJson: null);
        review.PublishedSchemaVersion = WorkflowMappers.ReadSchemaVersion(
            review.WorkflowSchemaJson);
        review.PublishedAt = now;
        review.SubmittedForReviewAt = null;
        review.LastReviewDecision = "approved";
        review.LastReviewedAt = now;
        review.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            AuditEntityTypes.WorkflowVersion,
            review.Id,
            "workflow.version.published",
            actorId,
            now,
            new
            {
                code = definition.Code,
                version,
                schemaVersion = review.PublishedSchemaVersion,
                contentHash = review.ContentHash,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToVersionDto(definition, review);
    }

    public async Task<WorkflowVersionDto> CreateDraftFromLatestAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        if (definition.Versions.Any(
                item => item.Status is WorkflowVersionStatus.Draft
                    or WorkflowVersionStatus.Review))
        {
            throw new ConflictException(
                $"Workflow '{code}' already has an editable version.");
        }

        WorkflowVersion? source = definition.Versions
            .Where(item => item.Status == WorkflowVersionStatus.Published
                && item.Version != null)
            .OrderBy(item => item.Version!, SemverRules.StringComparer)
            .LastOrDefault();
        DateTimeOffset now = timeProvider.GetUtcNow();
        var draft = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            WorkflowDefinitionId = definition.Id,
            Status = WorkflowVersionStatus.Draft,
            WorkflowSchemaJson = source?.WorkflowSchemaJson
                ?? DefaultWorkflowSchema,
            CreatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.WorkflowVersion,
            draft.Id,
            "workflow.draft.created",
            actorId,
            now,
            new
            {
                code = definition.Code,
                sourceVersion = source?.Version,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        workflows.AddVersion(draft);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToVersionDto(definition, draft);
    }

    public async Task<WorkflowVersionDto> RetireVersionAsync(
        string code,
        string version,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(version);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        SemverRules.EnsureValid(version);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        WorkflowVersion published = definition.Versions.SingleOrDefault(
                item => string.Equals(item.Version, version, StringComparison.Ordinal)
                    && item.Status == WorkflowVersionStatus.Published)
            ?? throw new NotFoundException(
                $"Published workflow '{code}' version '{version}' was not found.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        WorkflowVersionLifecycle.Fire(
            published,
            WorkflowVersionLifecycle.Trigger.Retire);
        published.RetiredAt = now;
        definition.UpdatedAt = now;
        auditWriter.Append(
            AuditEntityTypes.WorkflowVersion,
            published.Id,
            "workflow.version.retired",
            actorId,
            now,
            new
            {
                code = definition.Code,
                version,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkflowMappers.ToVersionDto(definition, published);
    }

    public async Task SoftDeleteDraftAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: true, hospitalContext.HospitalId, cancellationToken).ConfigureAwait(false);
        WorkflowVersion draft = WorkflowWorkflowHelpers.RequireDraft(definition);
        if (definition.Versions.Any(
                item => item.Status == WorkflowVersionStatus.Published))
        {
            throw new InvalidStateException(
                $"Workflow '{code}' cannot be soft-deleted while published versions exist. Retire active versions first.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        workflows.RemoveVersion(draft);
        definition.DeletedAt = now;
        definition.UpdatedAt = now;
        auditWriter.Append(
            AuditEntityTypes.WorkflowDefinition,
            definition.Id,
            "workflow.draft.deleted",
            actorId,
            now,
            new
            {
                code = definition.Code,
                draftVersionId = draft.Id,
                workflowDefinitionId = definition.Id,
            },
            workflowDefinitionId: definition.Id);

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
