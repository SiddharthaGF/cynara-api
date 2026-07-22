using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Forms.Persistence;
using Cynara.Application.Persistence;
using Cynara.Application.Schemas;
using Cynara.Domain.Forms;

namespace Cynara.Application.Forms;

public sealed class FormService(
    IFormRepository forms,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ISchemaValidator schemaValidator,
    TimeProvider timeProvider) : IFormService
{
    public async Task<FormSummaryDto> CreateAsync(CreateFormRequest request, string? actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FormCodeRules.EnsureValid(request.Code);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Form name is required.");
        }

        schemaValidator.ValidateFormDraft(request.ClinicalSchemaJson, request.UiSchemaJson, request.RulesSchemaJson);

        if (await forms.CodeExistsAsync(request.Code, cancellationToken).ConfigureAwait(false))
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

        auditWriter.Append("form-definition", definition.Id, "form.created", actorId, now, new
        {
            code = definition.Code,
            draftVersionId = draft.Id,
        });

        forms.AddDefinition(definition, draft);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToSummary(definition);
    }

    public async Task<IReadOnlyList<FormSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<FormDefinition> items = await forms.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        return [.. items.Select(FormMappers.ToSummary)];
    }

    public async Task<FormSummaryDto> GetSummaryAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        FormDefinition definition = await FormWorkflowHelpers.RequireDefinitionAsync(
            forms,
            code,
            track: false,
            cancellationToken).ConfigureAwait(false);
        return FormMappers.ToSummary(definition);
    }

    public async Task<FormVersionDto> GetEditableVersionAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        FormDefinition definition = await FormWorkflowHelpers.RequireDefinitionAsync(
            forms,
            code,
            track: false,
            cancellationToken).ConfigureAwait(false);
        FormVersion editable = FormWorkflowHelpers.RequireEditableVersion(definition);
        return FormMappers.ToVersionDto(definition, editable);
    }

    public async Task<FormVersionDto> GetVersionAsync(string code, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(version);
        SemverRules.EnsureValid(version);
        FormDefinition definition = await FormWorkflowHelpers.RequireDefinitionAsync(
            forms,
            code,
            track: false,
            cancellationToken).ConfigureAwait(false);
        FormVersion? published = definition.Versions.SingleOrDefault(
            item => item.Version == version && item.Status != FormVersionStatus.Draft && item.Status != FormVersionStatus.Review)
            ?? throw new NotFoundException($"Form '{code}' version '{version}' was not found.");
        return FormMappers.ToVersionDto(definition, published);
    }

    public async Task<FormVersionDto> UpdateDraftAsync(
        string code,
        UpdateFormDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        schemaValidator.ValidateFormDraft(request.ClinicalSchemaJson, request.UiSchemaJson, request.RulesSchemaJson);

        FormDefinition definition = await FormWorkflowHelpers.RequireDefinitionAsync(
            forms,
            code,
            track: true,
            cancellationToken).ConfigureAwait(false);
        FormVersion draft = FormWorkflowHelpers.RequireDraft(definition);
        FormWorkflowHelpers.EnsureDraftConcurrency(draft, request.RowVersion);

        draft.ClinicalSchemaJson = request.ClinicalSchemaJson;
        draft.UiSchemaJson = request.UiSchemaJson;
        draft.RulesSchemaJson = request.RulesSchemaJson;
        draft.RowVersion = request.RowVersion + 1;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        auditWriter.Append("form-version", draft.Id, "form.draft.updated", actorId, definition.UpdatedAt, new
        {
            code = definition.Code,
            rowVersion = request.RowVersion,
        });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToVersionDto(definition, draft);
    }

    public async Task<FormVersionDto> CreateDraftFromLatestAsync(string code, string? actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        FormDefinition definition = await FormWorkflowHelpers.RequireDefinitionAsync(
            forms,
            code,
            track: true,
            cancellationToken).ConfigureAwait(false);

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

        auditWriter.Append("form-version", draft.Id, "form.draft.created", actorId, now, new
        {
            code = definition.Code,
            sourceVersion = source?.Version,
        });

        forms.AddVersion(draft);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToVersionDto(definition, draft);
    }

    public async Task<FormVersionDto> RetireVersionAsync(
        string code,
        string version,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(version);
        SemverRules.EnsureValid(version);

        FormDefinition definition = await FormWorkflowHelpers.RequireDefinitionAsync(
            forms,
            code,
            track: true,
            cancellationToken).ConfigureAwait(false);
        FormVersion published = definition.Versions.SingleOrDefault(
            item => item.Version == version && item.Status == FormVersionStatus.Published)
            ?? throw new NotFoundException($"Published form '{code}' version '{version}' was not found.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        published.Status = FormVersionStatus.Retired;
        published.RetiredAt = now;
        definition.UpdatedAt = now;

        auditWriter.Append("form-version", published.Id, "form.version.retired", actorId, now, new
        {
            code = definition.Code,
            version,
        });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FormMappers.ToVersionDto(definition, published);
    }

    public async Task SoftDeleteDraftAsync(
        string code,
        string? reason,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        FormDefinition definition = await FormWorkflowHelpers.RequireDefinitionAsync(
            forms,
            code,
            track: true,
            cancellationToken).ConfigureAwait(false);
        FormVersion editable = FormWorkflowHelpers.RequireEditableVersion(definition);

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

        auditWriter.Append("form-definition", definition.Id, "form.draft.deleted", actorId, now, new
        {
            code = definition.Code,
            draftVersionId = editable.Id,
            reason,
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
                  "code": "form.placeholder",
                  "type": "text"
                }
              ]
            }
            """;
    }
}
