using Cynara.Application.Modules.FormAi;
using Cynara.Domain.FormAi;
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
/// Exposes AI provider settings without leaking the API key on reads.
/// Writes delegate to <see cref="IAiProviderSettingsService"/>.
/// </summary>
public sealed class AiProviderSettingsResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<AiProviderSettings> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IAiProviderSettingsService settingsService,
    CynaraDbContext dbContext)
    : JsonApiResourceService<AiProviderSettings, string>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
{
    public override Task<AiProviderSettings?> CreateAsync(
        AiProviderSettings resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "AI provider settings are a singleton. PATCH id 'default' instead.");
    }

    public override async Task<AiProviderSettings?> UpdateAsync(
        string id,
        AiProviderSettings resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!string.Equals(id, AiProviderSettings.DefaultId, StringComparison.Ordinal))
        {
            throw new Application.NotFoundException(
                $"AI provider settings '{id}' were not found.");
        }

        _ = await settingsService.UpsertAsync(
            new FormAiSettingsUpdateRequest(
                ApiKey: resource.ApiKey,
                BaseUrl: resource.BaseUrl,
                Model: resource.Model,
                JsonObject: resource.JsonObject),
            cancellationToken).ConfigureAwait(false);

        return await dbContext.AiProviderSettings
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == AiProviderSettings.DefaultId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task DeleteAsync(
        string id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "AI provider settings cannot be deleted.");
    }
}
