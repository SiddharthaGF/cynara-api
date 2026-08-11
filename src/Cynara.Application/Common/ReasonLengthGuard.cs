using System.Globalization;

namespace Cynara.Application.Common;

/// <summary>
/// Shared bounded-reason guard for workflow transitions. Module helpers keep
/// their facade; this class owns the canonical cap and message.
/// </summary>
internal static class ReasonLengthGuard
{
    public const int MaxReasonLength = 2000;

    public static string Normalize(string? reason, string entityLabel)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        string trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength)
        {
            throw new ValidationException(
                $"{entityLabel} transition reason must be "
                + MaxReasonLength.ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }

        return trimmed;
    }
}
