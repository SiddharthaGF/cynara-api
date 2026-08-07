using System.Globalization;

using Cynara.Domain.Tasks;

namespace Cynara.Application.Modules.Tasks;

/// <summary>
/// Shared validation and formatting helpers for the task runtime.
/// </summary>
internal static class TaskWorkflowHelpers
{
    public static void EnsureConcurrency(uint current, uint provided)
    {
        if (current != provided)
        {
            throw new ConcurrencyException(
                "The task was modified by another request.");
        }
    }

    public static ClinicalTaskStatus? ParseStatusOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out ClinicalTaskStatus parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Task status '" + value
                + "' is not one of: open, claimed, completed, canceled.");
        }

        return parsed;
    }

    public static string FormatStatus(ClinicalTaskStatus status)
    {
        return status switch
        {
            ClinicalTaskStatus.Open => "open",
            ClinicalTaskStatus.Claimed => "claimed",
            ClinicalTaskStatus.Completed => "completed",
            ClinicalTaskStatus.Canceled => "canceled",
            _ => status.ToString().ToLowerInvariant(),
        };
    }

    public static string EnsureReasonLength(string? reason)
    {
        const int maxLength = 2000;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        string trimmed = reason.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(
                "Task transition reason must be "
                + maxLength.ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }

        return trimmed;
    }
}
