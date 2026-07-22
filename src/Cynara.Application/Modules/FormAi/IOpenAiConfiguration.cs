namespace Cynara.Application.Modules.FormAi;

public sealed record OpenAiConfig(
    string? ApiKey,
    string BaseUrl,
    string Model,
    bool Configured,
    bool JsonObject);

public interface IOpenAiConfiguration
{
    public OpenAiConfig LoadEnvironment();
}
