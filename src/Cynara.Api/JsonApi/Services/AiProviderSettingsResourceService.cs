using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.FormAi;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.FormAi;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Services;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Exposes AI provider settings without leaking the API key on reads.
/// Reads project the resolved public view (DB or env fallback).
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
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard)
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

    public override async Task<IReadOnlyCollection<AiProviderSettings>> GetAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkspaceRead, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            await GetAsync(AiProviderSettings.DefaultId, cancellationToken)
                .ConfigureAwait(false),
        ];
    }

    public override async Task<AiProviderSettings> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        if (!string.Equals(id, AiProviderSettings.DefaultId, StringComparison.Ordinal))
        {
            throw new Application.NotFoundException(
                $"AI provider settings '{id}' were not found.");
        }

        FormAiSettingsResponse view = await settingsService
            .GetPublicViewAsync(cancellationToken)
            .ConfigureAwait(false);
        return Project(view);
    }

    public override async Task<AiProviderSettings?> UpdateAsync(
        string id,
        AiProviderSettings resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkspaceWrite, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(id, AiProviderSettings.DefaultId, StringComparison.Ordinal))
        {
            throw new Application.NotFoundException(
                $"AI provider settings '{id}' were not found.");
        }

        FormAiSettingsResponse view = await settingsService.UpsertAsync(
            new FormAiSettingsUpdateRequest(
                ApiKey: resource.ApiKey,
                ClearApiKey: resource.ClearApiKeyRequested,
                BaseUrl: resource.BaseUrl,
                Model: resource.Model,
                JsonObject: resource.JsonObject),
            cancellationToken).ConfigureAwait(false);

        return Project(view);
    }

    public override Task DeleteAsync(
        string id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "AI provider settings cannot be deleted.");
    }

    private static AiProviderSettings Project(FormAiSettingsResponse view)
    {
        return new AiProviderSettings
        {
            Id = AiProviderSettings.DefaultId,
            BaseUrl = view.BaseUrl,
            Model = view.Model,
            JsonObject = view.JsonObject,
            HasApiKey = view.ApiKeyConfigured,
            ApiKeyMasked = view.ApiKeyMasked,
            Configured = view.Configured,
            Source = view.Source,
            BaseUrlConfigured = view.BaseUrlConfigured,
            UpdatedAt = view.UpdatedAt ?? default,
            Suggestions =
            [
                .. view.Suggestions.Select(static item => new AiEndpointSuggestionAttr
                {
                    Id = item.Id,
                    Label = item.Label,
                    BaseUrl = item.BaseUrl,
                    DefaultModel = item.DefaultModel,
                    JsonObject = item.JsonObject,
                }),
            ],
        };
    }
}
