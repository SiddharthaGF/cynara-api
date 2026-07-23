using System.ComponentModel.DataAnnotations.Schema;

using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.FormAi;

/// <summary>
/// Singleton AI provider configuration (id = <see cref="DefaultId"/>).
/// The API key is write-only; clients only see <see cref="HasApiKey"/>.
/// </summary>
[Resource(
    PublicName = "aiProviderSettings",
    GenerateControllerEndpoints = JsonApiEndpoints.None)]
public sealed class AiProviderSettings : Identifiable<string>
{
    public const string DefaultId = "default";

    /// <summary>Raw API key storage; never exposed as a readable attribute.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Write-only API key accepted on PATCH/POST.</summary>
    [NotMapped]
    [Attr(PublicName = "apiKey", Capabilities = AttrCapabilities.AllowCreate | AttrCapabilities.AllowChange)]
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

    /// <summary>Whether an API key is currently stored (never reveals the secret).</summary>
    [NotMapped]
    [Attr(PublicName = "hasApiKey", Capabilities = AttrCapabilities.AllowView)]
    public bool HasApiKey
    {
        get => !string.IsNullOrWhiteSpace(ApiKey);
        set => _ = value;
    }

    [Attr(PublicName = "baseUrl")]
    public string? BaseUrl { get; set; }

    [Attr(PublicName = "model")]
    public string? Model { get; set; }

    [Attr(PublicName = "jsonObject")]
    public bool? JsonObject { get; set; }

    [Attr(PublicName = "updatedAt", Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public DateTimeOffset UpdatedAt { get; set; }
}
