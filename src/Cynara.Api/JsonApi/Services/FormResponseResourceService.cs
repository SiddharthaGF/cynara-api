using Cynara.Application.Common;
using Cynara.Application.Forms;
using Cynara.Application.Modules.FormResponses;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Forms;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Creates/updates/soft-deletes form responses through lifecycle services.
/// </summary>
public sealed class FormResponseResourceService(
    IFormResponseLifecycleService lifecycle,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<FormResponse> resourceChangeTracker)
    : TenantScopedResourceService<FormResponse, Guid>(
        deps,
        resourceChangeTracker)
{
    public override async Task<FormResponse?> CreateAsync(
        FormResponse resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.FormResponsesWrite,
            cancellationToken).ConfigureAwait(false);

        if (resource.FormVersion is null && resource.FormVersionId == Guid.Empty)
        {
            throw new Application.ValidationException(
                "formVersion relationship is required when creating a response.");
        }

        Guid versionId = resource.FormVersion?.Id ?? resource.FormVersionId;
        FormVersion formVersion = await DbContext.FormVersions
            .Include(item => item.FormDefinition)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == versionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Form version '{versionId}' was not found.");

        if (formVersion.HospitalId != HospitalId
            || formVersion.FormDefinition.HospitalId != HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form version '{versionId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(formVersion.Version))
        {
            throw new Application.NotFoundException(
                "Responses can only be created against published versions.");
        }

        FormResponseDto created = await lifecycle.CreateAsync(
            formVersion.FormDefinition.Code,
            formVersion.Version,
            new CreateFormResponseRequest(resource.AnswersJson),
            ActorId,
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(created.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<FormResponse?> UpdateAsync(
        Guid id,
        FormResponse resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.FormResponsesWrite,
            cancellationToken).ConfigureAwait(false);

        FormResponseDto updated = await lifecycle.UpdateAsync(
            id,
            new UpdateFormResponseRequest(
                resource.AnswersJson,
                resource.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(updated.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.FormResponsesWrite,
            cancellationToken).ConfigureAwait(false);
        string? reason = HttpContext?.Request.Query["reason"]
            .FirstOrDefault();
        await lifecycle.SoftDeleteDraftAsync(
            id,
            reason,
            ActorId,
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task<FormResponse> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.FormResponsesRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.FormResponses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId, item.DeletedAt))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwnedActive(ownership, id, "Form response");

        FormResponse? response = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (response is not null)
        {
            await RecordReadAuditAsync(
                id,
                AuditEntityTypes.FormResponse,
                "response.read",
                cancellationToken).ConfigureAwait(false);
        }

        return response!;
    }

    public override async Task<IReadOnlyCollection<FormResponse>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.FormResponsesRead,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<FormResponse> responses = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. responses.Where(item => item.HospitalId == HospitalId)];
    }
}
