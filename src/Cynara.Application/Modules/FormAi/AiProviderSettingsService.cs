using Cynara.Application.Persistence;
using Cynara.Domain.FormAi;

namespace Cynara.Application.Modules.FormAi;

public sealed class AiProviderSettingsService(
    Persistence.IAiProviderSettingsRepository repository,
    IOpenAiConfiguration environmentConfiguration,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAiProviderSettingsService
{
    private static readonly string DefaultOpenAiBaseUrl =
        HttpsApi("api.openai.com", "v1");

    private static readonly IReadOnlyList<AiEndpointSuggestion> Suggestions =
    [
        new(
            "openai",
            "OpenAI",
            DefaultOpenAiBaseUrl,
            "gpt-4o-mini",
            JsonObject: true),
        new(
            "minimax",
            "MiniMax",
            HttpsApi("api.minimax.io", "v1"),
            "MiniMax-M2.7",
            JsonObject: false),
        new(
            "openrouter",
            "OpenRouter",
            HttpsApi("openrouter.ai", "api/v1"),
            "openai/gpt-4o-mini",
            JsonObject: true),
    ];

    public async Task<OpenAiConfig> ResolveActiveConfigAsync(
        CancellationToken cancellationToken)
    {
        AiProviderSettings? row = await repository.GetAsync(cancellationToken).ConfigureAwait(false);
        return IsComplete(row?.ApiKey, row?.BaseUrl, row?.Model)
            ? MergeWithEnvironment(
                row!.ApiKey,
                row.BaseUrl,
                row.Model,
                row.JsonObject ?? true)
            : environmentConfiguration.LoadEnvironment();
    }

    public async Task<FormAiSettingsResponse> GetPublicViewAsync(
        CancellationToken cancellationToken)
    {
        AiProviderSettings? row = await repository.GetAsync(cancellationToken).ConfigureAwait(false);
        if (IsComplete(row?.ApiKey, row?.BaseUrl, row?.Model))
        {
            OpenAiConfig config = MergeWithEnvironment(
                row!.ApiKey,
                row.BaseUrl,
                row.Model,
                row.JsonObject ?? true);
            return ToSettingsResponse(config, "database", Suggestions, row.UpdatedAt);
        }

        OpenAiConfig environment = environmentConfiguration.LoadEnvironment();
        return environment.Configured
            ? ToSettingsResponse(environment, "env", Suggestions, row?.UpdatedAt)
            : new FormAiSettingsResponse(
                Configured: false,
                row?.Model?.Trim(),
                NormalizeOptionalBaseUrl(row?.BaseUrl),
                ApiKeyConfigured: false,
                ApiKeyMasked: null,
                row?.JsonObject ?? true,
                "none",
                !string.IsNullOrWhiteSpace(row?.BaseUrl),
                Suggestions,
                row?.UpdatedAt);
    }

    public async Task<FormAiSettingsResponse> UpsertAsync(
        FormAiSettingsUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AiProviderSettings? existing = await repository.GetAsync(cancellationToken).ConfigureAwait(false);
        string? apiKey = existing?.ApiKey;
        if (request.ClearApiKey)
        {
            apiKey = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            apiKey = request.ApiKey.Trim();
        }

        string? baseUrl = request.BaseUrl is null
            ? existing?.BaseUrl
            : NormalizeOptionalBaseUrl(request.BaseUrl);
        string? model = request.Model is null
            ? existing?.Model
            : NormalizeOptionalText(request.Model);

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ValidationException(
                $"Base URL is required (OpenAI-compatible endpoint, e.g. {DefaultOpenAiBaseUrl}).");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ValidationException("Model id is required.");
        }

        if (string.IsNullOrWhiteSpace(apiKey)
            && !request.ClearApiKey
            && string.IsNullOrWhiteSpace(existing?.ApiKey))
        {
            throw new ValidationException("API key is required.");
        }

        AiProviderSettings row = existing ?? new AiProviderSettings
        {
            Id = AiProviderSettings.DefaultId,
        };
        row.ApiKey = apiKey;
        row.BaseUrl = baseUrl;
        row.Model = model;
        row.JsonObject = request.JsonObject ?? existing?.JsonObject ?? true;
        row.UpdatedAt = timeProvider.GetUtcNow();

        if (existing is null)
        {
            repository.Add(row);
        }

        _ = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetPublicViewAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsComplete(
        string? apiKey,
        string? baseUrl,
        string? model)
    {
        return !string.IsNullOrWhiteSpace(apiKey)
            && !string.IsNullOrWhiteSpace(baseUrl)
            && !string.IsNullOrWhiteSpace(model);
    }

    private static OpenAiConfig Resolve(
        string? apiKey,
        string? baseUrl,
        string? model,
        bool jsonObject)
    {
        string normalizedBaseUrl = NormalizeOptionalBaseUrl(baseUrl)
            ?? DefaultOpenAiBaseUrl;
        string normalizedModel = NormalizeOptionalText(model) ?? "gpt-4o-mini";
        string? normalizedKey = NormalizeOptionalText(apiKey);
        return new OpenAiConfig(
            normalizedKey,
            normalizedBaseUrl,
            normalizedModel,
            !string.IsNullOrWhiteSpace(normalizedKey),
            jsonObject,
            NetworkTimeout: TimeSpan.FromMinutes(10),
            MaxOutputTokens: 8192,
            Temperature: 0.2f,
            TopP: 0.9f,
            FirstChunkTimeout: TimeSpan.FromSeconds(90));
    }

    /// <summary>
    /// Resolves a config from a stored row and applies environment runtime
    /// knobs without persisting them per form.
    /// </summary>
    private OpenAiConfig MergeWithEnvironment(
        string? apiKey,
        string? baseUrl,
        string? model,
        bool jsonObject)
    {
        OpenAiConfig env = environmentConfiguration.LoadEnvironment();
        OpenAiConfig row = Resolve(apiKey, baseUrl, model, jsonObject);
        return row with
        {
            NetworkTimeout = env.NetworkTimeout,
            MaxOutputTokens = env.MaxOutputTokens ?? row.MaxOutputTokens,
            Temperature = env.Temperature ?? row.Temperature,
            TopP = env.TopP ?? row.TopP,
            FirstChunkTimeout = env.FirstChunkTimeout,
        };
    }

    private static FormAiSettingsResponse ToSettingsResponse(
        OpenAiConfig config,
        string source,
        IReadOnlyList<AiEndpointSuggestion> suggestions,
        DateTimeOffset? updatedAt)
    {
        return new FormAiSettingsResponse(
            config.Configured,
            config.Model,
            config.BaseUrl,
            !string.IsNullOrWhiteSpace(config.ApiKey),
            MaskApiKey(config.ApiKey),
            config.JsonObject,
            source,
            !string.IsNullOrWhiteSpace(config.BaseUrl),
            suggestions,
            updatedAt);
    }

    private static string? NormalizeOptionalBaseUrl(string? value)
    {
        string? normalized = NormalizeOptionalText(value);
        return normalized?.TrimEnd('/');
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? MaskApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= 4 ? "****" : $"****{trimmed[^4..]}";
    }

    private static string HttpsApi(string host, string path)
    {
        return new UriBuilder
        {
            Scheme = Uri.UriSchemeHttps,
            Host = host,
            Path = path,
        }.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
