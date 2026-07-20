using System.Text.Json;

using Cynara.Application.Common;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Domain.Audit;
using Cynara.Domain.Components;

namespace Cynara.Application.Components;

public sealed class ComponentService(
    IComponentRepository components,
    IAuditRepository audit,
    ISchemaValidator schemaValidator,
    TimeProvider timeProvider) : IComponentService
{
    public async Task<ComponentSummaryDto> CreateAsync(CreateComponentRequest request, string? actorId, CancellationToken cancellationToken)
    {
        ComponentCodeRules.EnsureValid(request.Code);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Component name is required.");
        }

        schemaValidator.ValidateComponentDraft(request.ClinicalSchemaJson, request.UiSchemaJson);

        if (await components.CodeExistsAsync(request.Code, cancellationToken))
        {
            throw new ConflictException($"Component '{request.Code}' already exists.");
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

        AppendAudit("component-definition", definition.Id, "component.created", actorId, now, new
        {
            code = definition.Code,
            draftVersionId = draft.Id,
        });

        await components.AddDefinitionAsync(definition, draft, cancellationToken);
        return ToSummary(definition);
    }

    public async Task<IReadOnlyList<ComponentSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ComponentDefinition> items = await components.ListDefinitionsAsync(cancellationToken);
        return [.. items.Select(ToSummary)];
    }

    public async Task<ComponentSummaryDto> GetSummaryAsync(string code, CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken);
        return ToSummary(definition);
    }

    public async Task<ComponentVersionDto> GetDraftAsync(string code, CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken);
        ComponentVersion draft = RequireDraft(definition);
        return ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> GetVersionAsync(string code, string version, CancellationToken cancellationToken)
    {
        SemverRules.EnsureValid(version);
        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken);
        ComponentVersion? published = definition.Versions.SingleOrDefault(
            item => item.Version == version && item.Status != ComponentVersionStatus.Draft) ?? throw new NotFoundException($"Component '{code}' version '{version}' was not found.");
        return ToVersionDto(definition, published);
    }

    public async Task<ComponentVersionDto> UpdateDraftAsync(
        string code,
        UpdateComponentDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        schemaValidator.ValidateComponentDraft(request.ClinicalSchemaJson, request.UiSchemaJson);

        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        ComponentVersion draft = RequireDraft(definition);
        EnsureDraftConcurrency(draft, request.RowVersion);

        draft.ClinicalSchemaJson = request.ClinicalSchemaJson;
        draft.UiSchemaJson = request.UiSchemaJson;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        AppendAudit("component-version", draft.Id, "component.draft.updated", actorId, definition.UpdatedAt, new
        {
            code = definition.Code,
            rowVersion = request.RowVersion,
        });

        await components.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> PublishDraftAsync(
        string code,
        PublishComponentDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        ComponentVersion draft = RequireDraft(definition);
        EnsureDraftConcurrency(draft, request.RowVersion);

        schemaValidator.ValidateComponentDraft(draft.ClinicalSchemaJson, draft.UiSchemaJson);

        string version = SemverRules.NextVersion(
            definition.Versions
                .Where(item => item.Status == ComponentVersionStatus.Published && item.Version != null)
                .Select(item => item.Version!));

        DateTimeOffset now = timeProvider.GetUtcNow();
        draft.Version = version;
        draft.Status = ComponentVersionStatus.Published;
        draft.ContentHash = ContentHashCalculator.Compute(draft.ClinicalSchemaJson, draft.UiSchemaJson);
        draft.PublishedAt = now;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = now;

        AppendAudit("component-version", draft.Id, "component.version.published", actorId, now, new
        {
            code = definition.Code,
            version,
            contentHash = draft.ContentHash,
        });

        await components.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> CreateDraftFromLatestAsync(string code, string? actorId, CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);

        if (definition.Versions.Any(item => item.Status == ComponentVersionStatus.Draft))
        {
            throw new ConflictException($"Component '{code}' already has a draft version.");
        }

        ComponentVersion? source = definition.Versions
            .Where(item => item.Status == ComponentVersionStatus.Published && item.Version != null)
            .OrderBy(item => item.Version!, SemverRules.StringComparer)
            .LastOrDefault();

        DateTimeOffset now = timeProvider.GetUtcNow();
        var draft = new ComponentVersion
        {
            Id = Guid.NewGuid(),
            ComponentDefinitionId = definition.Id,
            Status = ComponentVersionStatus.Draft,
            ClinicalSchemaJson = source?.ClinicalSchemaJson ?? DefaultClinicalSchema(),
            UiSchemaJson = source?.UiSchemaJson,
            CreatedAt = now,
        };

        AppendAudit("component-version", draft.Id, "component.draft.created", actorId, now, new
        {
            code = definition.Code,
            sourceVersion = source?.Version,
        });

        await components.AddVersionAsync(draft, cancellationToken);
        return ToVersionDto(definition, draft);
    }

    public async Task<ComponentVersionDto> RetireVersionAsync(
        string code,
        string version,
        string? actorId,
        CancellationToken cancellationToken)
    {
        SemverRules.EnsureValid(version);

        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        ComponentVersion published = definition.Versions.SingleOrDefault(
            item => item.Version == version && item.Status == ComponentVersionStatus.Published)
            ?? throw new NotFoundException($"Published component '{code}' version '{version}' was not found.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        published.Status = ComponentVersionStatus.Retired;
        published.RetiredAt = now;
        definition.UpdatedAt = now;

        AppendAudit("component-version", published.Id, "component.version.retired", actorId, now, new
        {
            code = definition.Code,
            version,
        });

        await components.SaveChangesAsync(cancellationToken);
        return ToVersionDto(definition, published);
    }

    public async Task SoftDeleteDraftAsync(string code, string? actorId, CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await RequireDefinitionAsync(code, cancellationToken, track: true);
        ComponentVersion draft = RequireDraft(definition);

        bool hasPublishedVersions = definition.Versions.Any(item => item.Status == ComponentVersionStatus.Published);
        if (hasPublishedVersions)
        {
            throw new InvalidStateException(
                $"Component '{code}' cannot be soft-deleted while published versions exist. Retire active versions first.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        components.RemoveVersion(draft);
        definition.DeletedAt = now;
        definition.UpdatedAt = now;

        AppendAudit("component-definition", definition.Id, "component.draft.deleted", actorId, now, new
        {
            code = definition.Code,
            draftVersionId = draft.Id,
        });

        await components.SaveChangesAsync(cancellationToken);
    }

    private async Task<ComponentDefinition> RequireDefinitionAsync(
        string code,
        CancellationToken cancellationToken,
        bool track = false)
    {
        ComponentDefinition? definition = await components.FindDefinitionByCodeAsync(code, track, cancellationToken);
        return definition ?? throw new NotFoundException($"Component '{code}' was not found.");
    }

    private static ComponentVersion RequireDraft(ComponentDefinition definition)
    {
        return definition.Versions.SingleOrDefault(item => item.Status == ComponentVersionStatus.Draft)
            ?? throw new NotFoundException($"Component '{definition.Code}' has no draft version.");
    }

    private static void EnsureDraftConcurrency(ComponentVersion draft, uint expectedRowVersion)
    {
        if (draft.RowVersion != expectedRowVersion)
        {
            throw new ConcurrencyException("The component draft was modified by another request.");
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

    private static ComponentSummaryDto ToSummary(ComponentDefinition definition)
    {
        ComponentVersion? draft = definition.Versions.SingleOrDefault(item => item.Status == ComponentVersionStatus.Draft);
        var publishedVersions = definition.Versions
            .Where(item => item.Status == ComponentVersionStatus.Published && item.Version != null)
            .Select(item => item.Version!)
            .OrderBy(static version => version, SemverRules.StringComparer)
            .ToList();

        return new ComponentSummaryDto(
            definition.Code,
            definition.Name,
            definition.CreatedAt,
            definition.UpdatedAt,
            draft?.Id.ToString(),
            draft?.RowVersion,
            publishedVersions);
    }

    private static ComponentVersionDto ToVersionDto(ComponentDefinition definition, ComponentVersion version)
    {
        return new ComponentVersionDto(
            version.Id,
            definition.Code,
            version.Version,
            version.Status.ToString().ToLowerInvariant(),
            version.ClinicalSchemaJson,
            version.UiSchemaJson,
            version.ContentHash,
            version.RowVersion,
            version.CreatedAt,
            version.PublishedAt,
            version.RetiredAt);
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
