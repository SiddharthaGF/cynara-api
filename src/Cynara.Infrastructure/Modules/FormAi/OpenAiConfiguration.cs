using System.Globalization;

using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.Configuration;

namespace Cynara.Infrastructure.Modules.FormAi;

public sealed class OpenAiConfiguration(IConfiguration configuration)
    : IOpenAiConfiguration
{
    /// <summary>
    /// Default ceiling for a single chat request. Authoring big forms can take
    /// several minutes on MiniMax-M3; align the client
    /// (<c>AI_STREAM_TIMEOUT_MS</c>) to this value before changing it.
    /// </summary>
    internal static readonly TimeSpan DefaultNetworkTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Time the stream has to deliver its first chunk before we cancel and
    /// retry once. Long TTFBs are the main cause of client-side timeouts
    /// during big-form authoring.
    /// </summary>
    internal static readonly TimeSpan DefaultFirstChunkTimeout = TimeSpan.FromSeconds(90);

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
        TimeSpan networkTimeout = ParseSeconds(
            configuration["OPENAI_NETWORK_TIMEOUT_SECONDS"],
            DefaultNetworkTimeout);
        TimeSpan firstChunkTimeout = ParseSeconds(
            configuration["OPENAI_FIRST_CHUNK_TIMEOUT_SECONDS"],
            DefaultFirstChunkTimeout);
        int? maxOutputTokens = ParseInt(
            configuration["OPENAI_MAX_OUTPUT_TOKENS"]);
        float? temperature = ParseFloat(configuration["OPENAI_TEMPERATURE"]);
        float? topP = ParseFloat(configuration["OPENAI_TOP_P"]);
        string? normalizedKey = NormalizeText(apiKey);

        return new OpenAiConfig(
            normalizedKey,
            baseUrl,
            model,
            !string.IsNullOrWhiteSpace(normalizedKey),
            jsonObject,
            networkTimeout,
            maxOutputTokens,
            temperature,
            topP,
            firstChunkTimeout);
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

    private static TimeSpan ParseSeconds(string? value, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static float? ParseFloat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return float.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float parsed)
            ? parsed
            : null;
    }
}
