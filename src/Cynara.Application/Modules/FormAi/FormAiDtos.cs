namespace Cynara.Application.Modules.FormAi;

public sealed record FormAiChatMessage(string Role, string Content);

public sealed record FormAiChatRequest(
    IReadOnlyList<FormAiChatMessage>? Messages,
    string? Locale = null,
    IReadOnlyList<string>? FocusedFieldIds = null,
    IReadOnlyList<string>? FocusedFieldTypes = null,
    string? ClinicalSchemaJson = null,
    string? UiSchemaJson = null,
    string? RulesSchemaJson = null);

public sealed record FormAiChatResponse(
    string Summary,
    string AssistantMessage,
    string? Thinking,
    string ClinicalSchemaJson,
    string UiSchemaJson,
    string RulesSchemaJson);

public sealed record FormAiStatusResponse(
    bool Configured,
    string? Model,
    string? BaseUrl,
    bool ApiKeyConfigured,
    string? ApiKeyMasked,
    bool JsonObject,
    string Source,
    bool BaseUrlConfigured);

public sealed record FormAiSettingsResponse(
    bool Configured,
    string? Model,
    string? BaseUrl,
    bool ApiKeyConfigured,
    string? ApiKeyMasked,
    bool JsonObject,
    string Source,
    bool BaseUrlConfigured,
    IReadOnlyList<AiEndpointSuggestion> Suggestions,
    DateTimeOffset? UpdatedAt = null);

public sealed record FormAiSettingsUpdateRequest(
    string? ApiKey = null,
    bool ClearApiKey = false,
    string? BaseUrl = null,
    string? Model = null,
    bool? JsonObject = null);

public sealed record AiEndpointSuggestion(
    string Id,
    string Label,
    string BaseUrl,
    string DefaultModel,
    bool JsonObject);
