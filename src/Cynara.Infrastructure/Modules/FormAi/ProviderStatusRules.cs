namespace Cynara.Infrastructure.Modules.FormAi;

/// <summary>
/// Single source of truth for transient HTTP status codes from
/// OpenAI-compatible providers, shared by the Polly retry pipeline and
/// <see cref="OpenAiProviderErrorMapper"/>.
/// </summary>
internal static class ProviderStatusRules
{
    /// <summary>
    /// Returns <see langword="true"/> when the status indicates a transient
    /// provider failure that is safe to retry: 408, 429, or any
    /// server error in the 500-599 range.
    /// </summary>
    public static bool IsTransient(int status)
    {
        return status is 408 or 429 or (>= 500 and <= 599);
    }
}
