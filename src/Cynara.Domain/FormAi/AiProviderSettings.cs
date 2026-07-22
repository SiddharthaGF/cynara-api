namespace Cynara.Domain.FormAi;

public sealed class AiProviderSettings
{
    public const string DefaultId = "default";

    public string Id { get; set; } = DefaultId;

    public string? ApiKey { get; set; }

    public string? BaseUrl { get; set; }

    public string? Model { get; set; }

    public bool? JsonObject { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
