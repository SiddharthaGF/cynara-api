using System.Globalization;

using Cynara.Application.Common;

using Cynara.Domain.Documents;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Shared validation and formatting helpers for the clinical document
/// instance lifecycle.
/// </summary>
internal static class ClinicalDocumentWorkflowHelpers
{
    public static void EnsureValidAuthorId(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return;
        }

        if (actorId.Trim().Length > ClinicalDocumentFieldLimits.AuthorIdMaxLength)
        {
            throw new ValidationException(
                "Clinical document author identifier must be "
                + ClinicalDocumentFieldLimits.AuthorIdMaxLength
                    .ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }
    }

    public static void EnsureConcurrency(uint current, uint provided)
    {
        ConcurrencyGuard.Ensure(current, provided, "clinical document");
    }

    public static string EnsureEnteredInErrorReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(
                "A reason is required to enter a clinical document in error.");
        }

        string trimmed = reason.Trim();
        if (trimmed.Length > ClinicalDocumentFieldLimits.EnteredInErrorReasonMaxLength)
        {
            throw new ValidationException(
                "Clinical document entered-in-error reason must be "
                + ClinicalDocumentFieldLimits.EnteredInErrorReasonMaxLength
                    .ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }

        return trimmed;
    }

    public static string EnsureEnteredInErrorActor(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ValidationException(
                "An authenticated actor is required to enter a clinical "
                + "document in error.");
        }

        EnsureValidAuthorId(actorId);
        return actorId;
    }

    public static ClinicalDocumentStatus? ParseStatusOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value, "enteredInError", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "entered-in-error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "entered_in_error", StringComparison.OrdinalIgnoreCase))
        {
            return ClinicalDocumentStatus.EnteredInError;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out ClinicalDocumentStatus parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Clinical document status '" + value
                + "' is not one of: inProgress, completed, canceled, "
                + "enteredInError.");
        }

        return parsed;
    }

    public static string FormatStatus(ClinicalDocumentStatus status)
    {
        return status switch
        {
            ClinicalDocumentStatus.InProgress => "inProgress",
            ClinicalDocumentStatus.Completed => "completed",
            ClinicalDocumentStatus.Canceled => "canceled",
            ClinicalDocumentStatus.EnteredInError => "enteredInError",
            _ => status.ToString().ToLowerInvariant(),
        };
    }
}
