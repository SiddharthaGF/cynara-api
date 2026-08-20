namespace Cynara.Application.Modules.FormAi;

public static class OpenAiDefaults
{
    public const string BaseUrl = "https://api.openai.com/v1";
    public const string Model = "gpt-4o-mini";
    public const bool JsonObject = true;
    public const int MaxOutputTokens = 8192;
    public const float Temperature = 0.2f;
    public const float TopP = 0.9f;
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan FirstChunkTimeout = TimeSpan.FromSeconds(90);
}
