using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.Configuration;

namespace Cynara.Infrastructure.Modules.FormAi;

public sealed class OpenAiConfiguration(IConfiguration configuration)
    : IOpenAiConfiguration
{
    public OpenAiConfig LoadEnvironment()
    {
        string? apiKey = configuration["OPENAI_API_KEY"];
        string baseUrl = NormalizeBaseUrl(
            configuration["OPENAI_BASE_URL"]
            ?? "https://api.openai.com/v1");
        string model = string.IsNullOrWhiteSpace(configuration["OPENAI_MODEL"])
            ? "gpt-4o-mini"
            : configuration["OPENAI_MODEL"]!.Trim();
        bool jsonObject = ParseBoolean(
            configuration["OPENAI_JSON_OBJECT"],
            defaultValue: true);
        string? normalizedKey = NormalizeText(apiKey);

        return new OpenAiConfig(
            normalizedKey,
            baseUrl,
            model,
            !string.IsNullOrWhiteSpace(normalizedKey),
            jsonObject);
    }

    internal static string NormalizeBaseUrl(string value)
    {
        return value.Trim().TrimEnd('/');
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ParseBoolean(string? value, bool defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToUpperInvariant() switch
            {
                "1" or "TRUE" or "YES" => true,
                "0" or "FALSE" or "NO" => false,
                _ => defaultValue,
            };
    }
}
