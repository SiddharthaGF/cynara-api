using Cynara.Api.Common.ActorContext;
using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.FormResponses;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Services;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Creates/updates/soft-deletes form responses through lifecycle services.
/// </summary>
public sealed class FormResponseResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<FormResponse> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IFormResponseLifecycleService lifecycle,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor,
    ISensitiveReadAuditor sensitiveReadAuditor,
    ICapabilityGuard capabilityGuard,
    CynaraDbContext dbContext)
    : JsonApiResourceService<FormResponse, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
{
    public override async Task<FormResponse?> CreateAsync(
        FormResponse resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesWrite, cancellationToken)
            .ConfigureAwait(false);

        if (resource.FormVersion is null && resource.FormVersionId == Guid.Empty)
        {
            throw new Application.ValidationException(
                "formVersion relationship is required when creating a response.");
        }

        Guid versionId = resource.FormVersion?.Id ?? resource.FormVersionId;
        FormVersion formVersion = await dbContext.FormVersions
            .Include(item => item.FormDefinition)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == versionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Form version '{versionId}' was not found.");

        if (formVersion.HospitalId != hospitalContext.HospitalId
            || formVersion.FormDefinition.HospitalId != hospitalContext.HospitalId)
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
            httpContextAccessor.HttpContext?.GetActorId(),
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
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesWrite, cancellationToken)
            .ConfigureAwait(false);

        FormResponseDto updated = await lifecycle.UpdateAsync(
            id,
            new UpdateFormResponseRequest(
                resource.AnswersJson,
                resource.RowVersion),
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(updated.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesWrite, cancellationToken)
            .ConfigureAwait(false);
        string? reason = httpContextAccessor.HttpContext?.Request.Query["reason"]
            .FirstOrDefault();
        await lifecycle.SoftDeleteDraftAsync(
            id,
            reason,
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task<FormResponse> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesRead, cancellationToken)
            .ConfigureAwait(false);

        var ownership = await dbContext.FormResponses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId, item.DeletedAt })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null
            || ownership.HospitalId != hospitalContext.HospitalId
            || ownership.DeletedAt is not null)
        {
            throw new Application.NotFoundException(
                $"Form response '{id}' was not found.");
        }

        FormResponse? response = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (response is not null
            && httpContextAccessor.HttpContext is { } httpContext
            && HttpMethods.IsGet(httpContext.Request.Method))
        {
            await sensitiveReadAuditor.RecordAsync(
                AuditEntityTypes.FormResponse,
                id,
                "response.read",
                httpContext.GetActorId(),
                httpContext.Request.Path,
                cancellationToken).ConfigureAwait(false);
        }

        return response!;
    }

    public override async Task<IReadOnlyCollection<FormResponse>> GetAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.FormResponsesRead, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyCollection<FormResponse> responses = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. responses.Where(item => item.HospitalId == hospitalContext.HospitalId)];
    }
}
