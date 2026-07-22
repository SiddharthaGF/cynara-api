using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Components;
using Cynara.Application.Modules.Components.Persistence;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Domain.Components;

namespace Cynara.Application.Modules.Components;

public sealed class ComponentLifecycleService(
    IComponentRepository components,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ISchemaValidator schemaValidator,
    TimeProvider timeProvider) : IComponentLifecycleService
{
    public async Task<ComponentSummaryDto> CreateAsync(
        CreateComponentRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ComponentCodeRules.EnsureValid(request.Code);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Component name is required.");
        }

        schemaValidator.ValidateComponentDraft(
            request.ClinicalSchemaJson,
            request.UiSchemaJson);
        if (await components.CodeExistsAsync(request.Code, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Component '{request.Code}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var definition = new ComponentDefinition
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var draft = new ComponentVersion
        {
            Id = Guid.NewGuid(),
            ComponentDefinitionId = definition.Id,
            Status = ComponentVersionStatus.Draft,
            ClinicalSchemaJson = request.ClinicalSchemaJson,
            UiSchemaJson = request.UiSchemaJson,
            CreatedAt = now,
        };

        auditWriter.Append(
            "component-definition",
            definition.Id,
            "component.created",
            actorId,
            now,
            new
            {
                code = definition.Code,
                draftVersionId = draft.Id,
            });

        components.AddDefinition(definition, draft);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ComponentMappers.ToSummary(definition);
    }

    public async Task<ComponentVersionDto> UpdateDraftAsync(
        string code,
        UpdateComponentDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        schemaValidator.ValidateComponentDraft(
            request.ClinicalSchemaJson,
            request.UiSchemaJson);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, true, cancellationToken).ConfigureAwait(false);
        ComponentVersion draft = ComponentWorkflowHelpers.RequireDraft(definition);
        ComponentWorkflowHelpers.EnsureDraftConcurrency(
            draft,
            request.RowVersion);

        draft.ClinicalSchemaJson = request.ClinicalSchemaJson;
        draft.UiSchemaJson = request.UiSchemaJson;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        auditWriter.Append(
            "component-version",
            draft.Id,
            "component.draft.updated",
            actorId,
            definition.UpdatedAt,
            new
            {
                code = definition.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ComponentMappers.ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> PublishDraftAsync(
        string code,
        PublishComponentDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, true, cancellationToken).ConfigureAwait(false);
        ComponentVersion draft = ComponentWorkflowHelpers.RequireDraft(definition);
        ComponentWorkflowHelpers.EnsureDraftConcurrency(
            draft,
            request.RowVersion);
        schemaValidator.ValidateComponentDraft(
            draft.ClinicalSchemaJson,
            draft.UiSchemaJson);

        string version = SemverRules.NextVersion(
            definition.Versions
                .Where(item => item.Status == ComponentVersionStatus.Published
                    && item.Version != null)
                .Select(item => item.Version!));
        DateTimeOffset now = timeProvider.GetUtcNow();
        draft.Version = version;
        draft.Status = ComponentVersionStatus.Published;
        draft.ContentHash = ContentHashCalculator.Compute(
            draft.ClinicalSchemaJson,
            draft.UiSchemaJson);
        draft.PublishedAt = now;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        auditWriter.Append(
            "component-version",
            draft.Id,
            "component.version.published",
            actorId,
            now,
            new
            {
                code = definition.Code,
                version,
                contentHash = draft.ContentHash,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ComponentMappers.ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> CreateDraftFromLatestAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, true, cancellationToken).ConfigureAwait(false);
        if (definition.Versions.Any(
                item => item.Status == ComponentVersionStatus.Draft))
        {
            throw new ConflictException(
                $"Component '{code}' already has a draft version.");
        }

        ComponentVersion? source = definition.Versions
            .Where(item => item.Status == ComponentVersionStatus.Published
                && item.Version != null)
            .OrderBy(item => item.Version!, SemverRules.StringComparer)
            .LastOrDefault();
        DateTimeOffset now = timeProvider.GetUtcNow();
        var draft = new ComponentVersion
        {
            Id = Guid.NewGuid(),
            ComponentDefinitionId = definition.Id,
            Status = ComponentVersionStatus.Draft,
            ClinicalSchemaJson = source?.ClinicalSchemaJson
                ?? DefaultClinicalSchema(),
            UiSchemaJson = source?.UiSchemaJson,
            CreatedAt = now,
        };

        auditWriter.Append(
            "component-version",
            draft.Id,
            "component.draft.created",
            actorId,
            now,
            new
            {
                code = definition.Code,
                sourceVersion = source?.Version,
            });

        components.AddVersion(draft);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ComponentMappers.ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> RetireVersionAsync(
        string code,
        string version,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(version);
        SemverRules.EnsureValid(version);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, true, cancellationToken).ConfigureAwait(false);
        ComponentVersion published = definition.Versions.SingleOrDefault(
                item => item.Version == version
                    && item.Status == ComponentVersionStatus.Published)
            ?? throw new NotFoundException(
                $"Published component '{code}' version '{version}' was not found.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        published.Status = ComponentVersionStatus.Retired;
        published.RetiredAt = now;
        definition.UpdatedAt = now;
        auditWriter.Append(
            "component-version",
            published.Id,
            "component.version.retired",
            actorId,
            now,
            new
            {
                code = definition.Code,
                version,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ComponentMappers.ToVersionDto(definition, published);
    }

    public async Task SoftDeleteDraftAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ComponentDefinition definition = await ComponentWorkflowHelpers
            .RequireDefinitionAsync(components, code, true, cancellationToken).ConfigureAwait(false);
        ComponentVersion draft = ComponentWorkflowHelpers.RequireDraft(definition);
        if (definition.Versions.Any(
                item => item.Status == ComponentVersionStatus.Published))
        {
            throw new InvalidStateException(
                $"Component '{code}' cannot be soft-deleted while published versions exist. Retire active versions first.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        components.RemoveVersion(draft);
        definition.DeletedAt = now;
        definition.UpdatedAt = now;
        auditWriter.Append(
            "component-definition",
            definition.Id,
            "component.draft.deleted",
            actorId,
            now,
            new
            {
                code = definition.Code,
                draftVersionId = draft.Id,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string DefaultClinicalSchema()
    {
        return /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "fields": [
                {
                  "id": "placeholder",
                  "code": "component.placeholder",
                  "type": "text"
                }
              ]
            }
            """;
    }
}
