using System.ClientModel;
using System.Globalization;

using Cynara.Application;

namespace Cynara.Infrastructure.Modules.FormAi;

/// <summary>
/// Maps OpenAI-compatible provider failures to client-safe validation errors.
/// Provider messages must never be forwarded: they often echo API keys
/// (e.g. OpenAI 401 "Incorrect API key provided: sk-…").
/// </summary>
internal static class OpenAiProviderErrorMapper
{
    public static ValidationException Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ValidationException(ToSafeMessage(exception), exception);
    }

    internal static string ToSafeMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ClientResultException clientEx => FromStatus(clientEx.Status),
            HttpRequestException =>
                "Could not reach the AI provider. Check the base URL and network connectivity.",
            _ => "OpenAI-compatible provider request failed.",
        };
    }

    internal static string FromStatus(int status)
    {
        return status switch
        {
            401 or 403 =>
                "AI provider authentication failed. Check the API key in Settings.",
            404 =>
                "AI provider endpoint was not found. Check the base URL in Settings.",
            408 or 429 =>
                "AI provider is rate-limiting or timed out. Try again shortly.",
            >= 500 and <= 599 =>
                "AI provider is temporarily unavailable. Try again shortly.",
            > 0 => string.Create(
                CultureInfo.InvariantCulture,
                $"OpenAI-compatible request failed with HTTP {status}."),
            _ => "OpenAI-compatible provider request failed.",
        };
    }
}
