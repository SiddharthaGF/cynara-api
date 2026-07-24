using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.FormAi;

/// <summary>
/// Per-hospital AI provider configuration. The composite key
/// (<see cref="HospitalId"/>, <see cref="Id"/>) keeps landlord AI config
/// isolated by tenant. The API key is write-only; clients see
/// <see cref="HasApiKey"/> and <see cref="ApiKeyMasked"/> instead of the
/// secret. Rich view attrs (source, suggestions, configured) are projected
/// by the resource service from the active DB or environment fallback.
/// </summary>
[Resource(
    PublicName = "aiProviderSettings",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class AiProviderSettings : Identifiable<string>
{
    /// <summary>Canonical identifier for the per-hospital singleton row.</summary>
    public const string DefaultId = "default";

    /// <summary>Owning hospital workspace; part of the composite key.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>Raw API key storage; never exposed as a readable attribute.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Write-only API key accepted on PATCH/POST.</summary>
    [NotMapped]
    [Attr(
        PublicName = "apiKey",
        Capabilities = AttrCapabilities.AllowCreate | AttrCapabilities.AllowChange)]
    public string? ApiKeyWrite
    {
        get => null;
        set
        {
            if (value is not null)
            {
                ApiKey = value;
            }
        }
    }

    /// <summary>Write-only flag to remove a stored API key.</summary>
    [NotMapped]
    [Attr(PublicName = "clearApiKey", Capabilities = AttrCapabilities.AllowChange)]
    public bool ClearApiKey
    {
        get => false;
        set => ClearApiKeyRequested = value;
    }

    /// <summary>Tracks whether <see cref="ClearApiKey"/> was set on this PATCH.</summary>
    [NotMapped]
    [JsonIgnore]
    public bool ClearApiKeyRequested { get; private set; }

    /// <summary>Whether an API key is currently stored (never reveals the secret).</summary>
    [NotMapped]
    [Attr(PublicName = "hasApiKey", Capabilities = AttrCapabilities.AllowView)]
    public bool HasApiKey { get; set; }

    /// <summary>Masked API key indicator for admin UI (never the raw secret).</summary>
    [NotMapped]
    [Attr(PublicName = "apiKeyMasked", Capabilities = AttrCapabilities.AllowView)]
    public string? ApiKeyMasked { get; set; }

    /// <summary>Whether the active provider config is complete enough to call AI.</summary>
    [NotMapped]
    [Attr(PublicName = "configured", Capabilities = AttrCapabilities.AllowView)]
    public bool Configured { get; set; }

    /// <summary>Where the active config comes from: database, env, or none.</summary>
    [NotMapped]
    [Attr(PublicName = "source", Capabilities = AttrCapabilities.AllowView)]
    public string? Source { get; set; }

    /// <summary>Whether a base URL is present on the active config.</summary>
    [NotMapped]
    [Attr(PublicName = "baseUrlConfigured", Capabilities = AttrCapabilities.AllowView)]
    public bool BaseUrlConfigured { get; set; }

    /// <summary>Suggested OpenAI-compatible endpoints for the admin UI.</summary>
    [NotMapped]
    [Attr(PublicName = "suggestions", Capabilities = AttrCapabilities.AllowView)]
    public IList<AiEndpointSuggestionAttr> Suggestions { get; set; } = [];

    [Attr(PublicName = "baseUrl")]
    public string? BaseUrl { get; set; }

    [Attr(PublicName = "model")]
    public string? Model { get; set; }

    [Attr(PublicName = "jsonObject")]
    public bool? JsonObject { get; set; }

    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>JSON:API attribute DTO for endpoint suggestions.</summary>
public sealed class AiEndpointSuggestionAttr
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string DefaultModel { get; set; } = string.Empty;

    public bool JsonObject { get; set; }
}
