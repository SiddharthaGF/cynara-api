namespace Cynara.Application.Modules.FormAi;

public interface IOpenAiConfiguration
{
    public OpenAiConfig LoadEnvironment();
}

public sealed record OpenAiConfig(
    string? ApiKey,
    string BaseUrl,
    string Model,
    bool Configured,
    bool JsonObject,
    TimeSpan NetworkTimeout,
    int? MaxOutputTokens,
    float? Temperature,
    float? TopP,
    TimeSpan FirstChunkTimeout);
