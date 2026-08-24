using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

/// <summary>
/// Shared validation and formatting helpers for the pipeline runtime.
/// </summary>
internal static class PipelineWorkflowHelpers
{
    public static void EnsureConcurrency(uint current, uint provided)
    {
        ConcurrencyGuard.Ensure(current, provided, "pipeline");
    }

    public static PipelineSubjectType ParseSubjectType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.TryParse(value, ignoreCase: true, out PipelineSubjectType parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Pipeline subjectType '" + value
                + "' is not one of: encounter, patient.");
        }

        return parsed;
    }

    public static PipelineSubjectType? ParseSubjectTypeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseSubjectType(value);
    }

    public static PipelineStatus? ParseStatusOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value, "enteredInError", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "entered-in-error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "entered_in_error", StringComparison.OrdinalIgnoreCase))
        {
            return PipelineStatus.EnteredInError;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out PipelineStatus parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Pipeline status '" + value
                + "' is not one of: running, completed, canceled, "
                + "enteredInError.");
        }

        return parsed;
    }

    public static string FormatSubjectType(PipelineSubjectType type)
    {
        return type.ToString().ToLowerInvariant();
    }

    public static string FormatStatus(PipelineStatus status)
    {
        return status switch
        {
            PipelineStatus.Running => "running",
            PipelineStatus.Completed => "completed",
            PipelineStatus.Canceled => "canceled",
            PipelineStatus.EnteredInError => "enteredInError",
            _ => status.ToString().ToLowerInvariant(),
        };
    }

    public static string EnsureReasonLength(string? reason)
    {
        return ReasonLengthGuard.Normalize(reason, "Pipeline");
    }
}
